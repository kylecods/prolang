using System.Collections.Immutable;
using System.Reflection;
using ProLang.Documentation;
using ProLang.Interop;
using ProLang.Lowering;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Intermediate;

internal sealed class Binder
{
    private BoundScope? _scope;

    private readonly DiagnosticBag _diagnostics = new();

    private readonly bool _isScript;

    private readonly FunctionSymbol? _function;

    private Stack<(BoundLabel BreakLabel, BoundLabel ContinueLabel)> _loopStack = new();

    private int _labelCounter;

    private ImmutableArray<StructSymbol>.Builder? _structTypes;
    private ImmutableArray<EnumSymbol>.Builder? _enumTypes;

    // Shared across all binders in one compilation — maps concrete function name → (symbol, body).
    private readonly Dictionary<string, (FunctionSymbol Symbol, BoundBlockStatement Body)>? _sharedInstantiations;

    /// <summary>
    /// Where resolved names are written down for an editor to query, or null for a plain compile.
    /// </summary>
    /// <remarks>
    /// Shared by every binder in one compilation, and read by nothing during binding — it only
    /// ever accumulates. Null unless a caller asked for it, so the cost of it not being wanted is
    /// a null check.
    /// </remarks>
    private readonly BindingRecorder? _recorder;

    public Binder(bool isScript, BoundScope parent, FunctionSymbol? function,
        Dictionary<string, TypeSymbol>? typeBindings = null,
        Dictionary<string, (FunctionSymbol Symbol, BoundBlockStatement Body)>? sharedInstantiations = null,
        BindingRecorder? recorder = null)
    {
        _scope = new BoundScope(parent);
        _isScript = isScript;
        _function = function;
        _sharedInstantiations = sharedInstantiations;
        _recorder = recorder;

        if (typeBindings != null)
        {
            foreach (var (alias, concrete) in typeBindings)
                _scope.DeclareTypeBinding(alias, concrete);
        }

        if (function != null)
        {
            foreach (var parameter in function.Parameters)
            {
                _scope.TryDeclareVariable(parameter);
            }
        }
    }

    public static BoundGlobalScope BindGlobalScope(bool isScript, BoundGlobalScope? previous, ImmutableArray<SyntaxTree> syntaxTrees, ImmutableHashSet<string>? importedModules = null, BindingRecorder? recorder = null)
    {
        var parentScope = CreateParentScope(previous, importedModules);

        var binder = new Binder(isScript, parentScope, null, recorder: recorder);

        // Single-pass collection of all declarations to avoid multiple SelectMany iterations
        var allDeclarations = syntaxTrees.SelectMany(st => st.Root.Declarations).ToList();
        var structDeclarations = allDeclarations.OfType<StructDeclarationSyntax>();
        var enumDeclarations = allDeclarations.OfType<EnumDeclarationSyntax>();
        var functionDeclarations = allDeclarations.OfType<FunctionDeclarationSyntax>();
        var globalStatements = allDeclarations.OfType<GlobalStatementSyntax>();
        var globalVariableDeclarations = allDeclarations.OfType<GlobalVariableDeclarationSyntax>().ToList();

        foreach (var enumDecl in enumDeclarations)
        {
            binder.BindEnumDeclaration(enumDecl);
        }

        // Structs are declared in one pass and have their fields bound in a second, so that a field can
        // name a struct declared later in the file, or the struct it is declared in. See
        // Binder.DeclareStructDeclaration.
        var declaredStructs = new List<(StructDeclarationSyntax Syntax, StructSymbol Symbol)>();
        foreach (var structDecl in structDeclarations)
        {
            var structSymbol = binder.DeclareStructDeclaration(structDecl);
            if (structSymbol != null)
            {
                declaredStructs.Add((structDecl, structSymbol));
            }
        }

        foreach (var (structDecl, structSymbol) in declaredStructs)
        {
            binder.BindStructFields(structDecl, structSymbol);
        }

        binder.ReportValueStructCycles(declaredStructs);

        foreach (var function in functionDeclarations)
        {
            binder.BindFunctionDeclaration(function);
        }

        // Pre-register global variables from all files so they are visible
        // regardless of statement processing order (important for imports)
        binder.RegisterGlobalVariables(syntaxTrees);

        // Bind the `global` initializers now that every symbol exists. Done here rather than in
        // RegisterGlobalVariables so an initializer can reference a global declared in any file.
        var globalInitializers = binder.BindGlobalInitializers(globalVariableDeclarations.ToImmutableArray());

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        foreach (var statementSyntax in globalStatements)
        {
            var statement = binder.BindGlobalStatement(statementSyntax.Statement);

            statements.Add(statement);
        }

        //check any global Statements

        var firstGlobalStatementList = new List<GlobalDeclarationSyntax>();
        foreach (var syntaxTree in syntaxTrees)
        {
            var globalDecl = syntaxTree.Root.Declarations.OfType<GlobalDeclarationSyntax>().FirstOrDefault();
            if (globalDecl != null)
            {
                firstGlobalStatementList.Add(globalDecl);
            }
        }

        if (firstGlobalStatementList.Count > 1)
        {
            foreach (var globalStatement in firstGlobalStatementList)
            {
                binder.Diagnostics.ReportOnlyOneFileCanHaveGlobalStatements(globalStatement.Location);
            }
        }

        //Check for main/script with global statements

        var initialFunctions = binder._scope.GetDeclaredFunctions();

        FunctionSymbol mainFunction;
        FunctionSymbol userMainFunction;  // Track the user-defined main before renaming to __UserMain

        FunctionSymbol scriptFunction;

        if (isScript)
        {
            mainFunction = null;
            userMainFunction = null;

            if (globalStatements.Any())
            {
                scriptFunction = new FunctionSymbol("$eval", ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Any, null);
            }
            else
            {
                scriptFunction = null;
            }
        }
        else
        {
            var userMain = initialFunctions.FirstOrDefault(f => f.Name == "main");
            scriptFunction = null;

            if (userMain != null)
            {
                // Validate main function signature:
                // - Must return void
                // - Must have 0 parameters OR 1 parameter of type array<string>
                bool validSignature = userMain.Type == TypeSymbol.Void;

                if (validSignature && userMain.Parameters.Any())
                {
                    if (userMain.Parameters.Length != 1)
                    {
                        validSignature = false;
                    }
                    else
                    {
                        var param = userMain.Parameters[0];
                        // Check if parameter is array<string>
                        var arrayStringType = new TypeSymbol("array", ImmutableArray.Create(TypeSymbol.String));
                        if (param.Type != arrayStringType)
                        {
                            validSignature = false;
                        }
                    }
                }

                if (!validSignature)
                {
                    binder.Diagnostics.ReportMainFunctionMustHaveCorrectSignature(userMain.Declaration.Identifier.Location);
                }

                // Create __UserMain symbol with the same signature as main
                // This internal symbol will be the actual implementation
                userMainFunction = new FunctionSymbol(SyntheticNames.UserMain, userMain.Parameters, userMain.Type, userMain.Declaration);

                // Remove the user's "main" from the scope and replace with "__UserMain"
                // We need to update the scope to use __UserMain instead
                var declaredVariables = binder._scope.GetDeclaredVariables();
                binder._scope = new BoundScope(binder._scope.Parent);
                foreach (var fn in initialFunctions)
                {
                    if (fn.Name != "main")
                    {
                        binder._scope.TryDeclareFunction(fn);
                    }
                }
                binder._scope.TryDeclareFunction(userMainFunction);

                // Re-declare the top-level variables the old scope held — they were registered
                // before this rebuild and must survive it, or every function body would see an
                // undeclared name.
                foreach (var variable in declaredVariables)
                {
                    binder._scope.TryDeclareVariable(variable);
                }

                // Now set mainFunction to the user's main (will track as __UserMain)
                mainFunction = userMainFunction;
            }
            else
            {
                userMainFunction = null;
                mainFunction = null;
            }

            if (globalStatements.Any())
            {
                if (mainFunction != null)
                {
                    binder.Diagnostics.ReportCannotMixMainAndGlobalStatements(mainFunction.Declaration.Identifier.Location);

                    foreach (var globalStatement in globalStatements)
                    {
                        binder.Diagnostics.ReportCannotMixMainAndGlobalStatements(globalStatement.Location);
                    }
                }
                else
                {
                    // Global statements without explicit main() - error
                    // (they need to execute somewhere)
                    binder.Diagnostics.ReportGlobalStatementsRequireMainFunction(globalStatements.First().Location);
                }
            }
            // If no global statements and no main(), it's a library - this is allowed
            // Libraries can just define functions and types
        }

        // Get the updated functions list (may now contain __UserMain instead of main)
        var updatedFunctions = binder._scope.GetDeclaredFunctions();

        // Create synthetic __Main entry point if we have a user main function
        FunctionSymbol syntheticMainFunction = null;
        if (!isScript && mainFunction != null)
        {
            // Create synthetic __Main that will be the actual entry point
            // It takes string[] args from CLR but doesn't expose them to user
            syntheticMainFunction = new FunctionSymbol(SyntheticNames.Main, ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void, null);

            // Add synthetic __Main to scope and functions list
            binder._scope.TryDeclareFunction(syntheticMainFunction);
        }

        // Get final functions list (now includes __Main if it exists)
        var functions = binder._scope.GetDeclaredFunctions();

        var diagnostics = binder.Diagnostics.ToImmutableArray();
        var variables = binder._scope.GetDeclaredVariables();
        var structTypes = binder._structTypes?.ToImmutable() ?? ImmutableArray<StructSymbol>.Empty;
        var enumTypes = binder._enumTypes?.ToImmutable() ?? ImmutableArray<EnumSymbol>.Empty;

        if (previous != null)
        {
            diagnostics = diagnostics.InsertRange(0, previous.Diagnostics);
        }

        // Return the synthetic __Main as the main function (if it exists)
        // This ensures the entry point is properly set to __Main
        var finalMainFunction = syntheticMainFunction ?? mainFunction;

        return new BoundGlobalScope(previous, diagnostics, finalMainFunction, scriptFunction, functions, variables, statements.ToImmutableArray(), structTypes, importedModules, enumTypes,
            binder._scope.GetDeclaredVariables().OfType<GlobalVariableSymbol>().ToImmutableArray<VariableSymbol>(), globalInitializers);
    }

    /// <summary>
    /// Binds a generic function's body once, purely so its contents can be navigated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Code generation never binds a template — only its instantiations, one per set of type
    /// arguments. That is right for emitting and useless for an editor: a generic function nobody
    /// calls yet would have no symbols recorded inside it at all, so hovering anything in the body
    /// of <c>dynarray_push</c> would return nothing until somewhere else in the program happened
    /// to call it.
    /// </para>
    /// <para>
    /// So bind it with its type parameters standing for themselves, and throw everything away
    /// except what the recorder wrote down. The diagnostics in particular are discarded — they are
    /// reported against each real instantiation, and reporting them twice would double every error
    /// in a generic function.
    /// </para>
    /// </remarks>
    private static void BindGenericTemplateForAnalysis(bool isScript, BoundScope parentScope,
        FunctionSymbol function, BindingRecorder? recorder)
    {
        if (recorder == null || function.Declaration == null)
        {
            return;
        }

        var typeBindings = function.TypeParameters.ToDictionary(p => p.Name, p => (TypeSymbol)p);

        try
        {
            var binder = new Binder(isScript, parentScope, function, typeBindings, recorder: recorder);

            binder.BindStatement(function.Declaration.Body);
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // Analysing a template is a convenience, never a requirement. If a body only makes
            // sense once its type arguments are known, the instantiations still bind normally and
            // the editor is merely thinner inside this one function.
        }
    }

    public static BoundProgram BindProgram(bool isScript, BoundProgram previous, BoundGlobalScope? globalScope, BindingRecorder? recorder = null)
    {
        var parentScope = CreateParentScope(globalScope, globalScope?.ImportedModules);

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();

        // The same bodies before lowering, keeping if/while/for intact. Text backends emit from
        // these so their output reads like the source rather than like a jump table; the MSIL
        // emitter uses the lowered form. See BoundProgram.StructuredFunctions.
        var structuredBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundStatement>();

        // Shared registry for monomorphized generic function instantiations.
        // Keys are concrete function names; values are (symbol, lowered body).
        var sharedInstantiations = new Dictionary<string, (FunctionSymbol Symbol, BoundBlockStatement Body)>();

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var function in globalScope.Functions)
        {
            // Skip synthetic functions - they will be handled by the Emitter
            if (function.Declaration == null)
                continue;

            // Skip generic templates — they are instantiated on demand
            if (function.IsGeneric)
            {
                BindGenericTemplateForAnalysis(isScript, parentScope, function, recorder);
                continue;
            }

            var binder = new Binder(isScript, parentScope, function,
                sharedInstantiations: sharedInstantiations, recorder: recorder);

            var body = binder.BindStatement(function.Declaration.Body);

            var loweredBody = Lowerer.Lower(body);

            if (function.Type != TypeSymbol.Void && !ControlFlowGraph.AllPathsReturn(loweredBody))
            {
                binder._diagnostics.ReportAllPathsMustReturn(function.Declaration.Identifier.Location);
            }

            functionBodies.Add(function, loweredBody);
            structuredBodies.Add(function, body);

            diagnostics.AddRange(binder.Diagnostics);
        }

        if (globalScope.MainFunction != null && globalScope.Statements.Any())
        {
            var structured = new BoundBlockStatement(globalScope.Statements);

            functionBodies.Add(globalScope.MainFunction, Lowerer.Lower(structured));
            structuredBodies.Add(globalScope.MainFunction, structured);
        }
        else if (globalScope.ScriptFunction != null)
        {
            var statements = globalScope.Statements;

            if (statements.Length == 1 && statements[0] is BoundExpressionStatement es && es.Expression.Type != TypeSymbol.Void)
            {
                statements = statements.SetItem(0, new BoundReturnStatement(es.Expression));
            }
            else if (statements.Any() && statements.Last().Kind != BoundNodeKind.ReturnStatement)
            {
                var nullValue = new BoundLiteralExpression("");

                statements = statements.Add(new BoundReturnStatement(nullValue));
            }

            var structured = new BoundBlockStatement(statements);

            functionBodies.Add(globalScope.ScriptFunction, Lowerer.Lower(structured));
            structuredBodies.Add(globalScope.ScriptFunction, structured);
        }

        // Synthesize __GlobalsInit when the program declares any `global`. It runs the
        // initializers once before user code; backends call it from their entry path.
        if (globalScope.GlobalVariables.Any())
        {
            var initSymbol = new FunctionSymbol(
                SyntheticNames.GlobalsInit, ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void, null);

            var structured = new BoundBlockStatement(globalScope.GlobalInitializers);
            var lowered = Lowerer.Lower(structured);

            functionBodies.Add(initSymbol, lowered);
            structuredBodies.Add(initSymbol, structured);
        }

        // Add all collected generic instantiations to the function bodies
        foreach (var (_, (concreteSymbol, instBody)) in sharedInstantiations)
        {
            if (instBody != null && !functionBodies.ContainsKey(concreteSymbol))
                functionBodies.Add(concreteSymbol, instBody);
        }

        return new BoundProgram(previous, diagnostics.ToImmutable(), globalScope.MainFunction, globalScope.ScriptFunction, functionBodies.ToImmutable(), globalScope.StructTypes, globalScope.EnumTypes, structuredBodies.ToImmutable(),
            globalScope.GlobalVariables, globalScope.GlobalInitializers);
    }

    private void BindFunctionDeclaration(FunctionDeclarationSyntax syntax)
    {
        var typeParamSymbols = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
        foreach (var tp in syntax.TypeParameters)
            typeParamSymbols.Add(new TypeParameterSymbol(tp.Text, typeParamSymbols.Count));

        var savedScope = _scope;
        if (typeParamSymbols.Count > 0)
        {
            _scope = new BoundScope(_scope);
            foreach (var tp in typeParamSymbols)
                _scope.TryDeclareTypeSymbol(tp);
        }

        var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>();
        var seenParameterNames = new HashSet<string>();

        var sawOptionalParameter = false;

        foreach (var parameterSyntax in syntax.Parameters)
        {
            var parameterName = parameterSyntax.Identifier.Text;
            var parameterType = BindTypeClause(parameterSyntax.Type);

            object? defaultValue = null;

            if (parameterSyntax.DefaultValue != null)
            {
                defaultValue = BindConstantDefaultValue(parameterSyntax.DefaultValue, parameterName, parameterType);
                sawOptionalParameter = true;
            }
            else if (sawOptionalParameter)
            {
                // Optional parameters have to be a suffix of the list, or omitting one in the middle
                // would silently shift every positional argument after it.
                _diagnostics.ReportRequiredParameterAfterOptional(parameterSyntax.Location, parameterName);
            }

            if (!seenParameterNames.Add(parameterName))
            {
                _diagnostics.ReportParameterAlreadyDeclared(parameterSyntax.Location, parameterName);
            }
            else
            {
                var parameter = new ParameterSymbol(parameterName, parameterType, parameters.Count, defaultValue);
                parameters.Add(parameter);

                _recorder?.RecordSymbol(parameterSyntax.Identifier.Location, parameter, OccurrenceKind.Definition);
            }
        }

        var type = BindTypeClause(syntax.Type) ?? TypeSymbol.Void;

        _scope = savedScope;

        var function = new FunctionSymbol(syntax.Identifier.Text, parameters.ToImmutable(), type, syntax,
            typeParamSymbols.ToImmutable())
        {
            Documentation = DocumentationFor(syntax),
        };

        _recorder?.RecordSymbol(syntax.Identifier.Location, function, OccurrenceKind.Definition);

        if (!_scope.TryDeclareFunction(function))
        {
            _diagnostics.ReportSymbolAlreadyDeclared(syntax.Identifier.Location, function.Name);
        }
    }

    /// <summary>
    /// Binds a parameter's default value and reduces it to the constant the call site will use.
    /// </summary>
    /// <remarks>
    /// Restricted to constants deliberately. Storing a bound expression instead would mean
    /// re-emitting it in the caller's scope, where the names it referred to may not exist and,
    /// worse, may exist and mean something else. A constant has neither problem, and it is all a
    /// default value needs to be: <c>pad: int = 0</c>, <c>bold: bool = false</c>,
    /// <c>align: int = Align.START</c>.
    /// </remarks>
    private object? BindConstantDefaultValue(ExpressionSyntax syntax, string parameterName, TypeSymbol parameterType)
    {
        var bound = BindExpression(syntax);

        if (bound.Type == TypeSymbol.Error)
        {
            return null;
        }

        var converted = BindConversion(syntax.Location, bound, parameterType);
        var value = TryFoldConstant(converted);

        if (value == null)
        {
            _diagnostics.ReportDefaultValueMustBeConstant(syntax.Location, parameterName);
        }

        return value;
    }

    private static object? TryFoldConstant(BoundExpression expression)
    {
        switch (expression)
        {
            case BoundLiteralExpression literal:
                return literal.Value;

            // Enums erase to Int32, so a member is already the constant the caller wants.
            case BoundEnumMemberExpression member:
                return member.Member.Value;

            case BoundConversionExpression conversion:
                return TryFoldConstant(conversion.Expression);

            case BoundUnaryExpression unary:
                var operand = TryFoldConstant(unary.Operand);

                if (operand == null)
                {
                    return null;
                }

                return unary.Op.Kind switch
                {
                    BoundUnaryOperatorKind.Identity => operand,
                    BoundUnaryOperatorKind.Negation when operand is int i => -i,
                    BoundUnaryOperatorKind.Negation when operand is long l => -l,
                    BoundUnaryOperatorKind.Negation when operand is float f => -f,
                    BoundUnaryOperatorKind.Negation when operand is double d => -d,
                    BoundUnaryOperatorKind.LogicalNegation when operand is bool b => !b,
                    BoundUnaryOperatorKind.OnesComplement when operand is int i2 => ~i2,
                    _ => null,
                };

            // Folded because `0 - 1` is how this repository writes a negative literal, and because
            // a default like `cap: int = 4 * 1024` reads better than the number it works out to.
            case BoundBinaryExpression binary:
                return TryFoldBinaryConstant(binary);

            default:
                return null;
        }
    }

    private static object? TryFoldBinaryConstant(BoundBinaryExpression binary)
    {
        var left = TryFoldConstant(binary.Left);
        var right = TryFoldConstant(binary.Right);

        if (left == null || right == null)
        {
            return null;
        }

        if (left is bool lb && right is bool rb)
        {
            return binary.Op.Kind switch
            {
                BoundBinaryOperatorKind.LogicalAnd => lb && rb,
                BoundBinaryOperatorKind.LogicalOr => lb || rb,
                BoundBinaryOperatorKind.Equals => lb == rb,
                BoundBinaryOperatorKind.NotEquals => lb != rb,
                _ => null,
            };
        }

        if (left is string ls && right is string rs)
        {
            return binary.Op.Kind switch
            {
                BoundBinaryOperatorKind.Addition => ls + rs,
                BoundBinaryOperatorKind.Equals => ls == rs,
                BoundBinaryOperatorKind.NotEquals => ls != rs,
                _ => null,
            };
        }

        if (left is not int l || right is not int r)
        {
            return null;
        }

        // Division and modulo by zero would throw here rather than at the call site, which is not a
        // trade worth making inside the binder — leave them to be reported as non-constant.
        if (r == 0 && binary.Op.Kind is BoundBinaryOperatorKind.Division or BoundBinaryOperatorKind.Modulo)
        {
            return null;
        }

        return binary.Op.Kind switch
        {
            BoundBinaryOperatorKind.Addition => l + r,
            BoundBinaryOperatorKind.Subtraction => l - r,
            BoundBinaryOperatorKind.Multiplication => l * r,
            BoundBinaryOperatorKind.Division => l / r,
            BoundBinaryOperatorKind.Modulo => l % r,
            BoundBinaryOperatorKind.BitwiseAnd => l & r,
            BoundBinaryOperatorKind.BitwiseOr => l | r,
            BoundBinaryOperatorKind.BitwiseXor => l ^ r,
            BoundBinaryOperatorKind.BitwiseLeftShift => l << r,
            BoundBinaryOperatorKind.BitwiseRightShift => l >> r,
            BoundBinaryOperatorKind.Equals => l == r,
            BoundBinaryOperatorKind.NotEquals => l != r,
            BoundBinaryOperatorKind.LessThan => l < r,
            BoundBinaryOperatorKind.LessEqual => l <= r,
            BoundBinaryOperatorKind.GreaterThan => l > r,
            BoundBinaryOperatorKind.GreaterEqual => l >= r,
            _ => null,
        };
    }

    /// <summary>
    /// First pass: declares the struct's name, with its fields left unbound.
    /// </summary>
    /// <remarks>
    /// Field types are bound separately by <see cref="BindStructFields"/> once every struct name is in
    /// scope. Binding them here — as this did — resolves a field's type before its own struct exists,
    /// so a struct could not name itself, name a struct declared later in the file, or take part in a
    /// cycle of mutual references. Both emitters already tolerate all three: <c>TypeEmitter</c>
    /// registers the definition before adding fields, and <c>CEmitter</c> emits forward typedefs.
    /// </remarks>
    private StructSymbol? DeclareStructDeclaration(StructDeclarationSyntax syntax)
    {
        var name = syntax.Identifier.Text;

        var typeParameters = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
        foreach (var paramSyntax in syntax.TypeParameters)
        {
            var paramName = paramSyntax.Text;
            var typeParam = new TypeParameterSymbol(paramName, typeParameters.Count);
            typeParameters.Add(typeParam);
        }

        var structSymbol = StructSymbol.Declaring(
            name,
            typeParameters.ToImmutable(),
            syntax.IsReferenceType,
            DocumentationFor(syntax));

        _recorder?.RecordSymbol(syntax.Identifier.Location, structSymbol, OccurrenceKind.Definition);

        if (_structTypes == null)
        {
            _structTypes = ImmutableArray.CreateBuilder<StructSymbol>();
        }

        if (!_scope.TryDeclareType(structSymbol))
        {
            _diagnostics.ReportSymbolAlreadyDeclared(syntax.Identifier.Location, name);
            return null;
        }

        _structTypes.Add(structSymbol);
        return structSymbol;
    }

    /// <summary>
    /// Second pass: binds the field types of a struct declared by <see cref="DeclareStructDeclaration"/>
    /// and completes the same symbol instance.
    /// </summary>
    /// <remarks>
    /// Completing the instance created in the first pass, rather than building a second symbol, is what
    /// makes a forward reference whole: a field bound here may already hold the placeholder for a
    /// struct whose own fields are bound later, and because it is the same object that reference is
    /// retroactively complete.
    /// </remarks>
    private void BindStructFields(StructDeclarationSyntax syntax, StructSymbol structSymbol)
    {
        var name = syntax.Identifier.Text;

        var savedScope = _scope;
        if (structSymbol.TypeParameters.Length > 0)
        {
            _scope = new BoundScope(_scope);
            foreach (var typeParam in structSymbol.TypeParameters)
            {
                _scope.TryDeclareTypeSymbol(typeParam);
            }
        }

        var fields = ImmutableArray.CreateBuilder<StructField>();
        var seenFieldNames = new HashSet<string>();

        foreach (var fieldSyntax in syntax.Fields)
        {
            var fieldName = fieldSyntax.Identifier.Text;
            var fieldType = BindTypeClause(fieldSyntax.Type);

            if (!seenFieldNames.Add(fieldName))
            {
                _diagnostics.ReportDuplicateFieldName(fieldSyntax.Identifier.Location, name, fieldName);
            }
            else
            {
                var field = new StructField(fieldName, fieldType);
                fields.Add(field);

                _recorder?.RecordSymbol(fieldSyntax.Identifier.Location, field, OccurrenceKind.Definition);
            }
        }

        _scope = savedScope;

        structSymbol.CompleteFields(fields.ToImmutable());
    }

    /// <summary>
    /// Reports any struct that contains itself by value, directly or through a chain of other structs.
    /// </summary>
    /// <remarks>
    /// Such a struct has no finite size, and nothing downstream would catch it: <c>TypeEmitter</c>
    /// happily emits a value type whose field is that same value type, and the CLR rejects it at load
    /// time with a <c>TypeLoadException</c> rather than anything pointing at the source. The ordering
    /// bug that <see cref="DeclareStructDeclaration"/> fixes used to make this unreachable by reporting
    /// an undefined type instead.
    /// <para>
    /// Only a field whose type is itself a struct forms an edge. An <c>array&lt;T&gt;</c>,
    /// <c>map&lt;K, V&gt;</c>, function type, string, <c>any</c> or .NET type is a single reference of
    /// known size however deeply it nests, which is why <c>struct Ui { data: array&lt;int&gt; }</c> is
    /// legal today and must stay so.
    /// </para>
    /// </remarks>
    private void ReportValueStructCycles(List<(StructDeclarationSyntax Syntax, StructSymbol Symbol)> declared)
    {
        // Colour per symbol: absent = unvisited, false = on the current path, true = fully explored.
        var state = new Dictionary<StructSymbol, bool>();

        foreach (var (syntax, symbol) in declared)
        {
            Visit(symbol, syntax);
        }

        void Visit(StructSymbol symbol, StructDeclarationSyntax? syntax)
        {
            if (state.TryGetValue(symbol, out var finished))
            {
                if (!finished && syntax != null)
                {
                    _diagnostics.ReportStructCannotContainItself(syntax.Identifier.Location, symbol.Name);
                }

                return;
            }

            state[symbol] = false;

            foreach (var field in symbol.Fields)
            {
                // A class field is one reference of known size, exactly like an array field, so it
                // ends the chain rather than extending it. That is the whole point of `class Node {
                // next: Node }`.
                if (field.Type is not StructSymbol { IsReferenceType: false } fieldStruct)
                    continue;

                // A generic instantiation stands in for its template: `struct A<T> { x: A<int> }` is
                // the same cycle as a direct self-reference, and the instantiation is not itself a
                // declared symbol.
                var target = fieldStruct.OriginalGeneric ?? fieldStruct;

                if (state.TryGetValue(target, out var targetFinished) && !targetFinished)
                {
                    if (syntax != null)
                    {
                        _diagnostics.ReportStructCannotContainItself(syntax.Identifier.Location, symbol.Name);
                    }

                    continue;
                }

                Visit(target, declared.FirstOrDefault(d => d.Symbol == target).Syntax);
            }

            state[symbol] = true;
        }
    }

    private void BindEnumDeclaration(EnumDeclarationSyntax syntax)
    {
        var name = syntax.Identifier.Text;
        var members = ImmutableArray.CreateBuilder<EnumMember>();
        var seenNames = new HashSet<string>();
        int nextValue = 0;

        foreach (var memberSyntax in syntax.Members)
        {
            var memberName = memberSyntax.Identifier.Text;

            if (!seenNames.Add(memberName))
            {
                _diagnostics.ReportSymbolAlreadyDeclared(memberSyntax.Identifier.Location, memberName);
                continue;
            }

            if (memberSyntax.Initializer != null)
            {
                var initExpr = BindExpression(memberSyntax.Initializer);
                if (initExpr is BoundLiteralExpression literal && literal.Value is int explicitValue)
                {
                    nextValue = explicitValue;
                }
            }

            members.Add(new EnumMember(memberName, nextValue));
            nextValue++;
        }

        var enumSymbol = new EnumSymbol(name, members.ToImmutable())
        {
            Documentation = DocumentationFor(syntax),
        };

        if (_recorder != null)
        {
            _recorder.RecordSymbol(syntax.Identifier.Location, enumSymbol, OccurrenceKind.Definition);

            // Members are matched back to their syntax by name rather than by position, because a
            // duplicate member is skipped above and would otherwise shift every one after it.
            foreach (var memberSyntax in syntax.Members)
            {
                var member = enumSymbol.FindMember(memberSyntax.Identifier.Text);

                if (member != null)
                {
                    _recorder.RecordSymbol(memberSyntax.Identifier.Location,
                        _recorder.GetEnumMember(enumSymbol, member), OccurrenceKind.Definition);
                }
            }
        }

        _enumTypes ??= ImmutableArray.CreateBuilder<EnumSymbol>();

        if (!_scope.TryDeclareEnumType(enumSymbol))
        {
            _diagnostics.ReportSymbolAlreadyDeclared(syntax.Identifier.Location, name);
        }
        else
        {
            _enumTypes.Add(enumSymbol);
        }
    }

    private static BoundScope CreateParentScope(BoundGlobalScope? previous, ImmutableHashSet<string>? importedModules = null)
    {
        var stack = new Stack<BoundGlobalScope>();

        while (previous != null)
        {
            stack.Push(previous);
            previous = previous.Previous;
        }

        var parent = CreateRootScope(importedModules);

        while (stack.Count > 0)
        {
            previous = stack.Pop();
            var scope = new BoundScope(parent);

            foreach (var function in previous.Functions)
            {
                scope.TryDeclareFunction(function);
            }

            foreach (var variable in previous.Variables)
            {
                scope.TryDeclareVariable(variable);
            }

            foreach (var enumType in previous.EnumTypes)
            {
                scope.TryDeclareEnumType(enumType);
            }

            foreach (var structType in previous.StructTypes)
            {
                scope.TryDeclareType(structType);
            }
            parent = scope;
        }

        return parent;
    }

    private static BoundScope CreateRootScope(ImmutableHashSet<string>? importedModules = null)
    {
        var result = new BoundScope(null!);

        if (importedModules == null)
        {
            return result;
        }
        
        foreach (var moduleName in importedModules)
        {
            if (BuiltInModule.TryGetModule(moduleName, out var module) && module != null)
            {
                foreach (var f in module.Functions)
                {
                    result.TryDeclareFunction(f);
                }

                // For .NET modules, also register types that can be used as type names
                if (module is DotNetInteropModule dotNetModule)
                {
                    foreach (var type in dotNetModule.GetDotNetTypes())
                    {
                        // Register static methods with qualified names like "Math.Max"
                        foreach (var func in module.Functions)
                        {
                            result.TryDeclareFunction(func);
                        }
                    }
                }
            }
        }

        return result;
    }

    private DiagnosticBag Diagnostics => _diagnostics;

    private void RegisterGlobalVariables(ImmutableArray<SyntaxTree> syntaxTrees)
    {
        var globalStatements =
            syntaxTrees.SelectMany(st => st.Root.Declarations).OfType<GlobalStatementSyntax>();

        foreach (var gs in globalStatements)
        {
            if (gs.Statement is VariableStatementSyntax varStmt)
            {
                var type = BindTypeClause(varStmt.TypeClause);
                var initializer = type != null ? BindExpression(varStmt.Expression, type) : BindExpression(varStmt.Expression);
                var variableType = type ?? initializer.Type;
                BindVariable(varStmt.Identifier, false, variableType);
            }
        }

        // Declare every `global` across all files before any function body is bound, so a
        // function can reference a global declared in an imported file regardless of order.
        // Initializers are bound afterwards, once the symbols exist.
        foreach (var decl in syntaxTrees
                     .SelectMany(st => st.Root.Declarations)
                     .OfType<GlobalVariableDeclarationSyntax>())
        {
            var type = BindTypeClause(decl.TypeClause);

            if (type == null)
            {
                _diagnostics.ReportGlobalRequiresType(decl.Identifier.Location, decl.Identifier.Text ?? "?");
                continue;
            }

            BindVariable(decl.Identifier, false, type);
        }
    }

    /// <summary>
    /// Binds one assignment statement per <c>global</c>, in declaration order.
    /// </summary>
    /// <remarks>
    /// Called after <see cref="RegisterGlobalVariables"/> has declared every symbol across all
    /// files, so an initializer may reference a global declared earlier — including one from
    /// another file. The statements are emitted by the backends as the body of
    /// <see cref="SyntheticNames.GlobalsInit"/>, which runs once before user code.
    /// </remarks>
    private ImmutableArray<BoundStatement> BindGlobalInitializers(
        ImmutableArray<GlobalVariableDeclarationSyntax> declarations)
    {
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        foreach (var decl in declarations)
        {
            // The symbol was declared during registration; look it up rather than minting a new
            // one so that references inside other initializers and function bodies see the same
            // symbol.
            if (!_scope.TryLookupVariable(decl.Identifier.Text ?? "?", out var variable) || variable == null)
            {
                continue;
            }

            var initializer = BindExpression(decl.Expression, variable.Type);
            var converted = BindConversion(decl.Expression.Location, initializer, variable.Type);

            statements.Add(new BoundExpressionStatement(
                new BoundAssignmentExpression(variable, converted)));
        }

        return statements.ToImmutable();
    }

    private BoundStatement BindErrorStatement()
    {
        return new BoundExpressionStatement(new BoundErrorExpression());
    }

    private BoundStatement BindGlobalStatement(StatementSyntax syntax)
    {
        return BindStatement(syntax, isGlobal: true);
    }

    private BoundStatement BindStatement(StatementSyntax syntax, bool isGlobal = false)
    {
        var result = BindStatementInternal(syntax);

        if (!_isScript || !isGlobal)
        {
            if (result is BoundExpressionStatement es)
            {
                var isAllowedExpression = es.Expression.Kind == BoundNodeKind.BoundErrorExpression ||
                                            es.Expression.Kind == BoundNodeKind.BoundCallExpression ||
                                            es.Expression.Kind == BoundNodeKind.BoundIndirectCallExpression ||
                                            es.Expression.Kind == BoundNodeKind.BoundAssignmentExpression ||
                                            es.Expression.Kind == BoundNodeKind.BoundIndexAssignmentExpression ||
                                            es.Expression.Kind == BoundNodeKind.BoundFieldAssignmentExpression;

                if (!isAllowedExpression)
                {
                    _diagnostics.ReportInvalidExpressionStatement(syntax.Location);
                }
            }
        }
        return result;
    }

    private BoundStatement BindStatementInternal(StatementSyntax syntax)
    {
        switch (syntax.Kind)
        {
            case SyntaxKind.BlockStatement:
                {
                    return BindProLangBlockStatement((BlockStatementSyntax)syntax);
                }
            case SyntaxKind.VariableDeclaration:
                {
                    return BindVariableSyntax((VariableStatementSyntax)syntax);
                }
            case SyntaxKind.ExpressionStatement:
                {
                    return BindExpressionSyntax((ExpressionStatementSyntax)syntax);
                }
            case SyntaxKind.IfStatement:
                {
                    return BindIfStatementSyntax((IfStatementSyntax)syntax);
                }
            case SyntaxKind.WhileStatement:
                {
                    return BindWhileStatementSyntax((WhileStatementSyntax)syntax);
                }
            case SyntaxKind.ForStatement:
                {
                    return BindForStatementSyntax((ForStatementSyntax)syntax);
                }
            case SyntaxKind.BreakStatement:
                {
                    return BindBreakStatement((BreakStatementSyntax)syntax);
                }
            case SyntaxKind.ContinueStatement:
                {
                    return BindContinueStatement((ContinueStatementSyntax)syntax);
                }
            case SyntaxKind.ReturnStatement:
                {
                    return BindReturnStatement((ReturnStatementSyntax)syntax);
                }
            default:
                throw new Exception($"Unexpected syntax {syntax.Kind}");
        }
    }

    private BoundStatement BindReturnStatement(ReturnStatementSyntax syntax)
    {
        var expression = syntax.Expression == null ? null : BindExpression(syntax.Expression);

        if (_function == null)
        {
            if (_isScript)
            {
                if(expression == null)
                {
                    expression = new BoundLiteralExpression("");
                }
            }else if(expression != null)
            {
                _diagnostics.ReportInvalidReturnExpression(syntax.Expression.Location, _function.Name);
            }
        }
        else
        {
            if (_function.Type == TypeSymbol.Void)
            {
                // Bare "return;" is valid in a void function — nothing to do.
                // "return <expr>;" in a void function is an error.
                if (expression != null)
                {
                    _diagnostics.ReportInvalidReturnExpression(syntax.Expression!.Location, _function.Name);
                }
            }
        }

        return new BoundReturnStatement(expression);
    }

    private BoundStatement BindContinueStatement(ContinueStatementSyntax syntax)
    {
        if (_loopStack.Count == 0)
        {
            _diagnostics.ReportInvalidBreakOrContinue(syntax.Keyword.Location, syntax.Keyword.Text);

            return BindErrorStatement();
        }

        var continueLabel = _loopStack.Peek().ContinueLabel;

        return new BoundGotoStatement(continueLabel);
    }

    private BoundStatement BindBreakStatement(BreakStatementSyntax syntax)
    {
        if (_loopStack.Count == 0)
        {
            _diagnostics.ReportInvalidBreakOrContinue(syntax.Keyword.Location, syntax.Keyword.Text);

            return BindErrorStatement();
        }

        var breakLabel = _loopStack.Peek().BreakLabel;

        return new BoundGotoStatement(breakLabel);
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, bool canBeVoid = false)
    {
        return BindExpression(syntax, null, canBeVoid);
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, TypeSymbol? expectedType, bool canBeVoid = false)
    {
        var result = BindInternalExpression(syntax, expectedType);

        // Every expression in the program passes through here, which is the whole reason the type
        // map is recorded at this one point: it gives a type to spans no symbol occurrence covers.
        // Completion after a `.` needs the type of whatever is to its left, and that is as often
        // `getBox(i)` or `arr[0]` as it is a plain name.
        _recorder?.RecordExpressionType(syntax.Location, result.Type);

        if (!canBeVoid && result.Type == TypeSymbol.Void)
        {
            _diagnostics.ReportExpressionMustHaveValue(syntax.Location);
            return new BoundErrorExpression();
        }

        return result;
    }

    private BoundStatement BindLoopBody(StatementSyntax body, out BoundLabel breakLabel, out BoundLabel continueLabel)
    {
        _labelCounter++;

        breakLabel = new BoundLabel($"break{_labelCounter}");
        continueLabel = new BoundLabel($"countinue{_labelCounter}");

        _loopStack.Push((breakLabel, continueLabel));

        var boundBody = BindStatement(body);
        _loopStack.Pop();

        return boundBody;
    }

    private BoundStatement BindForStatementSyntax(ForStatementSyntax syntax)
    {
        var lowerBound = BindExpression(syntax.LowerBound, TypeSymbol.Int);
        var upperBound = BindExpression(syntax.UpBound, TypeSymbol.Int);

        _scope = new BoundScope(_scope);

        var variable = BindVariable(syntax.Identifier, true, TypeSymbol.Int);

        var body = BindLoopBody(syntax.Body, out var breakLabel, out var continueLabel);

        return new BoundForStatement(variable, lowerBound, upperBound, body, breakLabel, continueLabel);
    }

    private BoundStatement BindWhileStatementSyntax(WhileStatementSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition, TypeSymbol.Bool);
        var body = BindLoopBody(syntax.Body, out var breakLabel, out var continueLabel);

        return new BoundWhileStatement(condition, body, breakLabel, continueLabel);
    }

    private BoundStatement BindIfStatementSyntax(IfStatementSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition, TypeSymbol.Bool);
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Statement);
        var elseIfStatement = syntax.ElseIf == null ? null : BindElIfStatement(syntax.ElseIf);
        var elseStatement = syntax.Else == null ? null : BindStatement(syntax.Else.Body);

        return new BoundIfStatement(condition, body, elseIfStatement, elseStatement);
    }

    private BoundStatement? BindElIfStatement(ElseIfClauseSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition, TypeSymbol.Bool);
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundElIfStatement(condition, body);
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, TypeSymbol targetType)
    {
        return BindConversion(syntax, targetType);
    }

    private BoundStatement BindExpressionSyntax(ExpressionStatementSyntax syntax)
    {
        var expression = BindExpression(syntax.Expression, true);

        return new BoundExpressionStatement(expression);
    }

    private BoundStatement BindVariableSyntax(VariableStatementSyntax syntax)
    {
        var type = BindTypeClause(syntax.TypeClause);
        var initializer = type != null ? BindExpression(syntax.Expression, type) : BindExpression(syntax.Expression);

        var variableType = type ?? initializer.Type;

        // `let x = null` would otherwise give x the null type, which nothing can be assigned to and
        // no backend can represent. The type has to be written down.
        if (variableType == TypeSymbol.Null)
        {
            _diagnostics.ReportCannotInferTypeFromNull(syntax.Identifier.Location, syntax.Identifier.Text);
            variableType = TypeSymbol.Error;
        }

        var variable = BindVariable(syntax.Identifier, false, variableType);

        var convertedInitializer = BindConversion(syntax.Expression.Location, initializer, variableType);

        return new BoundVariableDeclaration(variable, convertedInitializer);
    }

    private TypeSymbol? BindTypeClause(TypeClauseSyntax? syntax)
    {
        if (syntax == null)
        {
            return null;
        }

        return BindTypeSyntax(syntax.Type);
    }

    private TypeSymbol BindTypeSyntax(TypeSyntax syntax)
    {
        if (syntax is NameTypeSyntax nameSyntax)
        {
            var name = nameSyntax.Identifier.Text;
            var type = LookupType(name);
            if (type == null)
            {
                _diagnostics.ReportUndefinedType(nameSyntax.Identifier.Location, name);
                return TypeSymbol.Error;
            }

            // This is the occurrence that makes `let p: Point` navigable, and it is only
            // obtainable here: a type clause produces a TypeSymbol and never a bound node, so
            // there is nowhere else in the pipeline that knows both this span and this symbol.
            _recorder?.RecordSymbol(nameSyntax.Identifier.Location, type, OccurrenceKind.TypeReference);

            return type;
        }

        if (syntax is GenericTypeSyntax genericSyntax)
        {
            var name = genericSyntax.Identifier.Text;
            var baseType = LookupType(name);
            if (baseType == null)
            {
                _diagnostics.ReportUndefinedType(genericSyntax.Identifier.Location, name);
                return TypeSymbol.Error;
            }

            // The template, before instantiation below — `DynArray` in `DynArray<int>` is written
            // once in the source and declared once, however many element types it is used with.
            _recorder?.RecordSymbol(genericSyntax.Identifier.Location, baseType, OccurrenceKind.TypeReference);

            var arguments = ImmutableArray.CreateBuilder<TypeSymbol>();
            foreach (var argSyntax in genericSyntax.Arguments)
            {
                arguments.Add(BindTypeSyntax(argSyntax));
            }

            if (baseType is StructSymbol structSymbol)
            {
                return structSymbol.InstantiateGeneric(arguments.ToArray());
            }

            return baseType.WithArgs(arguments.ToArray());
        }

        if (syntax is ArrayTypeSyntax arraySyntax)
        {
            var elementType = BindTypeSyntax(arraySyntax.ElementType);
            return TypeSymbol.Array.WithArgs(elementType);
        }

        if (syntax is FunctionTypeSyntax functionSyntax)
        {
            var parameterTypes = ImmutableArray.CreateBuilder<TypeSymbol>();

            foreach (var parameterSyntax in functionSyntax.ParameterTypes)
            {
                parameterTypes.Add(BindTypeSyntax(parameterSyntax));
            }

            var returnType = functionSyntax.ReturnType == null
                ? TypeSymbol.Void
                : BindTypeSyntax(functionSyntax.ReturnType.Type);

            if (parameterTypes.Count > FunctionTypeSymbol.MaxParameterCount)
            {
                _diagnostics.ReportFunctionValueTooManyParameters(functionSyntax.Location,
                    FunctionTypeSymbol.MaxParameterCount);
                return TypeSymbol.Error;
            }

            return new FunctionTypeSymbol(parameterTypes.ToImmutable(), returnType);
        }

        throw new Exception($"Unexpected type syntax {syntax.Kind}");
    }

    private TypeSymbol? LookupType(string name)
    {
        // From the shared table rather than a switch of its own. The switch this replaces had
        // drifted: it had no uint32 case, although TypeSymbol.UInt32 has always existed and all
        // three backends emit it, so `let x: uint32 = 0` was the one integer width that would not
        // bind. A table both this and completion read cannot drift again.
        if (TypeSymbol.Primitives.TryGetValue(name, out var primitive))
        {
            return primitive;
        }

        if (_scope.TryLookupTypeSymbol(name, out var typeSymbol))
        {
            return typeSymbol;
        }

        if (_scope.TryLookupType(name, out var structSymbol))
        {
            return structSymbol;
        }

        // Try to find as a .NET type
        var dotNetType = DotNetAssemblyRegistry.Instance.FindType(name);
        if (dotNetType != null)
        {
            return DotNetTypeMapper.MapToProLangType(dotNetType);
        }

        // Try common namespace prefixes
        foreach (var ns in new[] { "System", "System.Collections.Generic", "System.Text", "System.IO" })
        {
            dotNetType = DotNetAssemblyRegistry.Instance.FindTypeByNamespace(ns, name);
            if (dotNetType != null)
            {
                return DotNetTypeMapper.MapToProLangType(dotNetType);
            }
        }

        return null;
    }

    private BoundStatement BindProLangBlockStatement(BlockStatementSyntax syntax)
    {
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        _scope = new BoundScope(_scope);

        foreach (var statementSyntax in syntax.Statements)
        {
            var statement = BindStatement(statementSyntax);
            statements.Add(statement);
        }

        _scope = _scope.Parent;

        return new BoundBlockStatement(statements.ToImmutableArray());
    }

    public BoundExpression BindInternalExpression(ExpressionSyntax syntax, TypeSymbol? expectedType = null)
    {
        switch (syntax.Kind)
        {
            case SyntaxKind.ParethensisExpression:
                return BindParenthesizedExpression((ParenthesisExpressionSyntax)syntax);
            case SyntaxKind.NameExpression:
                return BindNameExpression((NameExpressionSyntax)syntax);
            case SyntaxKind.AssignmentExpression:
                return BindAssignmentExpression((AssignmentExpressionSyntax)syntax);
            case SyntaxKind.LiteralExpression:
                return BindLiteralExpression((LiteralExpressionSyntax)syntax);
            case SyntaxKind.UnaryExpression:
                return BindUnaryExpression((UnaryExpressionSyntax)syntax);
            case SyntaxKind.BinaryExpression:
                return BindBinaryExpression((BinaryExpressionSyntax)syntax);
            case SyntaxKind.CallExpression:
                return BindCallExpression((CallExpressionSyntax)syntax, expectedType);
            case SyntaxKind.ArrayExpression:
                return BindArrayExpression((ArrayExpressionSyntax)syntax, expectedType);
            case SyntaxKind.MapExpression:
                return BindMapExpression((MapExpressionSyntax)syntax, expectedType);
            case SyntaxKind.IndexExpression:
                return BindIndexExpression((IndexExpressionSyntax)syntax);
            case SyntaxKind.MethodCallExpression:
                return BindMethodCallExpression((MethodCallExpressionSyntax)syntax);
            case SyntaxKind.StructCreationExpression:
                return BindStructCreationExpression((StructCreationExpressionSyntax)syntax);
            case SyntaxKind.FieldAccessExpression:
                return BindFieldAccessExpression((FieldAccessExpressionSyntax)syntax);
            case SyntaxKind.CastExpression:
                return BindCastExpression((CastExpressionSyntax)syntax);
            default:
                throw new Exception($"Unknown syntax kind {syntax.Kind}");
        }
    }

    private BoundExpression BindArrayExpression(ArrayExpressionSyntax syntax, TypeSymbol? expectedType = null)
    {
        var elementType = TypeSymbol.Any;
        if (expectedType != null && expectedType.Name == "array" && expectedType.TypeArguments.Length == 1)
        {
            elementType = expectedType.TypeArguments[0];
        }

        var boundElements = ImmutableArray.CreateBuilder<BoundExpression>();
        foreach (var element in syntax.Elements)
        {
            var boundElement = BindExpression(element);
            var convertedElement = BindConversion(element.Location, boundElement, elementType);
            boundElements.Add(convertedElement);
        }
        
        var resultType = TypeSymbol.Array.WithArgs(elementType);
        return new BoundArrayExpression(boundElements.ToImmutable(), resultType);
    }

    private BoundExpression BindMapExpression(MapExpressionSyntax syntax, TypeSymbol? expectedType = null)
    {
        var keyType = TypeSymbol.Any;
        var valueType = TypeSymbol.Any;

        if (expectedType != null && expectedType.Name == "map" && expectedType.TypeArguments.Length == 2)
        {
            keyType = expectedType.TypeArguments[0];
            valueType = expectedType.TypeArguments[1];
        }

        var boundEntries = ImmutableArray.CreateBuilder<(BoundExpression Key, BoundExpression Value)>();
        foreach (var entry in syntax.Entries)
        {
            var key = BindExpression(entry.Key);
            var convertedKey = BindConversion(entry.Key.Location, key, keyType);
            var value = BindExpression(entry.Value);
            var convertedValue = BindConversion(entry.Value.Location, value, valueType);
            boundEntries.Add((convertedKey, convertedValue));
        }
        
        var resultType = TypeSymbol.Map.WithArgs(keyType, valueType);
        return new BoundMapExpression(boundEntries.ToImmutable(), resultType);
    }

    private BoundExpression BindIndexExpression(IndexExpressionSyntax syntax)
    {
        var expression = BindExpression(syntax.Expression);
        var index = BindExpression(syntax.Index);
        
        var resultType = TypeSymbol.Any;

        if (expression.Type.Name == "array")
        {
            index = BindConversion(syntax.Index.Location, index, TypeSymbol.Int);
            if (expression.Type.TypeArguments.Length == 1)
            {
                resultType = expression.Type.TypeArguments[0];
            }
        }
        else if (expression.Type.Name == "map")
        {
            var keyType = TypeSymbol.Any;
            if (expression.Type.TypeArguments.Length == 2)
            {
                keyType = expression.Type.TypeArguments[0];
                resultType = expression.Type.TypeArguments[1];
            }
            index = BindConversion(syntax.Index.Location, index, keyType);
        }

        return new BoundIndexExpression(expression, index, resultType);
    }

    private BoundExpression BindAssignmentExpression(AssignmentExpressionSyntax syntax)
    {
        var boundLhs = BindExpression(syntax.Left);
        var boundRhs = BindExpression(syntax.Right);

        // Desugar compound operators: x += y  →  x = x + y
        if (syntax.OperatorToken.Kind != SyntaxKind.EqualsToken)
        {
            var binaryOpKind = syntax.OperatorToken.Kind switch
            {
                SyntaxKind.PlusEqualsToken  => SyntaxKind.PlusToken,
                SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
                SyntaxKind.StarEqualsToken  => SyntaxKind.StarToken,
                SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
                _ => throw new Exception($"Unexpected compound assignment operator {syntax.OperatorToken.Kind}")
            };

            // Try exact match first, then numeric promotion (e.g. uint8 -= int → int - int)
            var boundOp = BoundBinaryOperator.Bind(binaryOpKind, boundLhs.Type, boundRhs.Type);
            var rhsForBinary = boundRhs;
            if (boundOp == null)
            {
                boundOp = BoundBinaryOperator.BindWithPromotion(
                    binaryOpKind, boundLhs.Type, boundRhs.Type,
                    out var promotedLeft, out var promotedRight);

                if (boundOp != null)
                {
                    // Promote the operands so the binary expression is well-typed.
                    var lhsForBinary = promotedLeft  != null
                        ? (BoundExpression)new BoundConversionExpression(promotedLeft,  boundLhs)
                        : boundLhs;
                    rhsForBinary = promotedRight != null
                        ? new BoundConversionExpression(promotedRight, boundRhs)
                        : boundRhs;
                    boundRhs = new BoundBinaryExpression(lhsForBinary, boundOp, rhsForBinary);
                }
            }
            else
            {
                boundRhs = new BoundBinaryExpression(boundLhs, boundOp, rhsForBinary);
            }

            if (boundOp == null)
            {
                _diagnostics.ReportUndefinedBinaryOperator(syntax.OperatorToken.Location, syntax.OperatorToken.Text, boundLhs.Type, boundRhs.Type);
                return new BoundErrorExpression();
            }
        }

        if (boundLhs is BoundVariableExpression variableExpression)
        {
            var variable = variableExpression.Variable;
            var convertedExpression = BindConversion(syntax.Right.Location, boundRhs, variable.Type);
            return new BoundAssignmentExpression(variable, convertedExpression);
        }
        else if (boundLhs is BoundIndexExpression indexExpression)
        {
            var expression = indexExpression.Expression;
            var index = indexExpression.Index;
            var valueType = indexExpression.Type;
            var value = BindConversion(syntax.Right.Location, boundRhs, valueType);

            return new BoundIndexAssignmentExpression(expression, index, value);
        }
        else if (boundLhs is BoundFieldAccessExpression fieldAccess)
        {
            var value = BindConversion(syntax.Right.Location, boundRhs, fieldAccess.Field.Type);
            return new BoundFieldAssignmentExpression(fieldAccess.Expression, fieldAccess.FieldName, fieldAccess.Field, value);
        }

        _diagnostics.ReportInvalidAssignmentTarget(syntax.Left.Location);
        return boundRhs;
    }

    private BoundExpression BindStructCreationExpression(StructCreationExpressionSyntax syntax)
    {
        var typeName = syntax.TypeName.Text;
        var structType = LookupType(typeName) as StructSymbol;

        if (structType == null)
        {
            _diagnostics.ReportUndefinedType(syntax.TypeName.Location, typeName);
            return new BoundErrorExpression();
        }

        // Instantiate generic struct if type arguments are provided: DynArray<int> { ... }
        if (syntax.TypeArguments.Length > 0)
        {
            var typeArgs = syntax.TypeArguments.Select(t => BindTypeSyntax(t)).ToArray();
            structType = structType.InstantiateGeneric(typeArgs) as StructSymbol ?? structType;
        }

        _recorder?.RecordSymbol(syntax.TypeName.Location, structType, OccurrenceKind.TypeReference);

        var fieldValues = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var initializer in syntax.Initializers)
        {
            var fieldName = initializer.FieldName.Text;
            var matchedField = structType.Fields.FirstOrDefault(f => f.Name == fieldName);
            var expectedFieldType = matchedField?.Type;
            var value = BindExpression(initializer.Expression, expectedType: expectedFieldType);
            if (matchedField != null)
                value = BindConversion(initializer.Expression.Location, value, matchedField.Type);
            fieldValues.Add(value);

            _recorder?.RecordSymbol(initializer.FieldName.Location, matchedField, OccurrenceKind.Reference);
        }

        return new BoundStructCreationExpression(structType, fieldValues.ToImmutable());
    }

    private BoundExpression BindFieldAccessExpression(FieldAccessExpressionSyntax syntax)
    {
        // Check if left side is an enum type name — must intercept before binding as variable
        if (syntax.Expression is NameExpressionSyntax nameExpr)
        {
            var candidateName = nameExpr.IdentifierToken.Text;
            if (_scope.TryLookupEnumType(candidateName, out var enumSym) && enumSym != null)
            {
                var memberName = syntax.FieldName.Text;
                var member = enumSym.FindMember(memberName);
                if (member == null)
                {
                    _diagnostics.ReportUndefinedField(syntax.FieldName.Location, enumSym.Name, memberName);
                    return new BoundErrorExpression();
                }

                if (_recorder != null)
                {
                    _recorder.RecordSymbol(nameExpr.IdentifierToken.Location, enumSym, OccurrenceKind.TypeReference);
                    _recorder.RecordSymbol(syntax.FieldName.Location,
                        _recorder.GetEnumMember(enumSym, member), OccurrenceKind.Reference);
                }

                return new BoundEnumMemberExpression(enumSym, member);
            }

            // A .NET static property or field read as `Type.Member`, e.g. DateTime.Now or
            // String.Empty. DotNetInteropModule registers these as zero-argument functions named
            // "Type.Member"; without this they fell through to variable binding and were
            // reported as "Variable 'DateTime' does not exist", which named the wrong problem.
            //
            // A real variable of the same name wins, so a local cannot be shadowed by a .NET type.
            if (!_scope.TryLookupVariable(candidateName, out _)
                && TryBindDotNetStaticMember(candidateName, syntax.FieldName.Text) is { } staticMember)
            {
                return staticMember;
            }
        }

        var expression = BindExpression(syntax.Expression);

        if (expression.Type == TypeSymbol.Error)
        {
            return new BoundErrorExpression();
        }

        var fieldName = syntax.FieldName.Text;

        // If the expression type is already a StructSymbol (e.g., a concrete generic instantiation),
        // use it directly without going through the scope name lookup.
        StructSymbol? structType;
        if (expression.Type is StructSymbol directStruct)
        {
            structType = directStruct;
        }
        else if (!_scope.TryLookupType(expression.Type.Name, out structType))
        {
            _diagnostics.ReportInvalidFieldAccess(syntax.Expression.Location, expression.Type);
            return new BoundErrorExpression();
        }

        var field = structType!.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null)
        {
            _diagnostics.ReportUndefinedField(syntax.FieldName.Location, structType.Name, fieldName);
            return new BoundErrorExpression();
        }

        _recorder?.RecordSymbol(syntax.FieldName.Location, field, OccurrenceKind.Reference);

        return new BoundFieldAccessExpression(expression, fieldName, field);
    }

    /// <summary>
    /// Binds a read of a .NET static property or field written as <c>Type.Member</c>.
    /// </summary>
    /// <returns>
    /// A call to the member's accessor, or <see langword="null"/> if no such member exists —
    /// in which case the caller carries on with ordinary field-access binding.
    /// </returns>
    /// <remarks>
    /// Static properties and fields are exposed as zero-argument functions rather than as a
    /// distinct symbol kind, because reading either compiles to a call or a field load with no
    /// arguments. The emitter already distinguishes them: <c>InteropEmitter.EmitCall</c> checks
    /// for a <c>MethodInfo</c> first, then a field, then a property getter.
    /// </remarks>
    private BoundExpression? TryBindDotNetStaticMember(string typeName, string memberName)
    {
        // Interop members are resolved through the assembly registry rather than the scope's
        // function table, which only holds ProLang functions and builtins.
        var member = ResolveDotNetStaticMethod(typeName, memberName, argumentCount: 0);

        if (member == null)
        {
            return null;
        }

        // Only a member that takes no arguments can be read without a call. A method written
        // without parentheses is a mistake, not a property read, and should keep reporting as one.
        if (!member.Parameters.IsEmpty)
        {
            return null;
        }

        return new BoundCallExpression(member, ImmutableArray<BoundExpression>.Empty);
    }

    private BoundExpression BindCastExpression(CastExpressionSyntax syntax)
    {
        var expression = BindExpression(syntax.Expression);
        var targetType = BindTypeClause(syntax.Type);

        if (expression.Type == TypeSymbol.Error || targetType == null || targetType == TypeSymbol.Error)
        {
            return new BoundErrorExpression();
        }

        // Cast expressions only work with 'any' type as source
        if (expression.Type != TypeSymbol.Any)
        {
            _diagnostics.ReportCanOnlyCastFromAnyType(syntax.Location, expression.Type);
            return new BoundErrorExpression();
        }

        return new BoundCastExpression(expression, targetType);
    }

    private BoundExpression BindParenthesizedExpression(ParenthesisExpressionSyntax syntax)
    {
        return BindExpression(syntax.Expression);
    }

    private BoundExpression BindBinaryExpression(BinaryExpressionSyntax syntax)
    {
        var boundLeft  = BindExpression(syntax.Left);
        var boundRight = BindExpression(syntax.Right);

        if (boundLeft.Type == TypeSymbol.Error || boundRight.Type == TypeSymbol.Error)
            return new BoundErrorExpression();

        // Comparing against null is handled ahead of the operator table, which matches on exact
        // operand types and so cannot express "any reference type, against null".
        if (boundLeft.Type == TypeSymbol.Null || boundRight.Type == TypeSymbol.Null)
        {
            return BindNullComparison(syntax, boundLeft, boundRight);
        }

        var boundOperator = BoundBinaryOperator.Bind(syntax.OperatorToken.Kind, boundLeft.Type, boundRight.Type);

        // Fallback: try numeric promotion (e.g. uint8 - int → int - int)
        if (boundOperator == null)
        {
            boundOperator = BoundBinaryOperator.BindWithPromotion(
                syntax.OperatorToken.Kind,
                boundLeft.Type,
                boundRight.Type,
                out var promotedLeft,
                out var promotedRight);

            if (boundOperator != null)
            {
                if (promotedLeft  != null) boundLeft  = new BoundConversionExpression(promotedLeft,  boundLeft);
                if (promotedRight != null) boundRight = new BoundConversionExpression(promotedRight, boundRight);
            }
        }

        if (boundOperator == null)
        {
            _diagnostics.ReportUndefinedBinaryOperator(syntax.OperatorToken.Location, syntax.OperatorToken.Text, boundLeft.Type, boundRight.Type);
            return new BoundErrorExpression();
        }

        return new BoundBinaryExpression(boundLeft, boundOperator, boundRight);
    }

    /// <summary>
    /// Binds <c>x == null</c> / <c>x != null</c> (in either order) as a reference comparison.
    /// </summary>
    /// <remarks>
    /// <see cref="BoundBinaryOperator.Bind"/> scans a static table keyed on exact operand types, so
    /// it can say "int == int" but not "any reference type == null". Nothing needs to change in the
    /// emitter: it already falls through to a bare <c>ceq</c> for operands that are not strings, and
    /// <c>ceq</c> against <c>ldnull</c> is precisely a reference comparison.
    /// <para>
    /// <c>any</c> is allowed as the other operand because an `any`-typed value genuinely can hold a
    /// null reference, and a boxed zero is not reference-equal to one.
    /// </para>
    /// </remarks>
    private BoundExpression BindNullComparison(BinaryExpressionSyntax syntax, BoundExpression boundLeft, BoundExpression boundRight)
    {
        var operatorKind = syntax.OperatorToken.Kind;

        if (operatorKind != SyntaxKind.EqualsEqualsToken && operatorKind != SyntaxKind.BangEqualsToken)
        {
            _diagnostics.ReportUndefinedBinaryOperator(
                syntax.OperatorToken.Location, syntax.OperatorToken.Text, boundLeft.Type, boundRight.Type);

            return new BoundErrorExpression();
        }

        var otherType = boundLeft.Type == TypeSymbol.Null ? boundRight.Type : boundLeft.Type;

        var comparable =
            otherType == TypeSymbol.Null ||
            otherType == TypeSymbol.Any ||
            otherType is DotNetTypeSymbol ||
            otherType is StructSymbol { IsReferenceType: true };

        if (!comparable)
        {
            _diagnostics.ReportCannotCompareWithNull(syntax.OperatorToken.Location, otherType);
            return new BoundErrorExpression();
        }

        var boundOperator = BoundBinaryOperator.ReferenceEquality(operatorKind, boundLeft.Type, boundRight.Type);

        return new BoundBinaryExpression(boundLeft, boundOperator, boundRight);
    }

    private BoundExpression BindUnaryExpression(UnaryExpressionSyntax syntax)
    {
        var boundOperand = BindExpression(syntax.Operand);

        if (boundOperand.Type == TypeSymbol.Error)
        {
            return new BoundErrorExpression();
        }

        var boundOperator = BoundUnaryOperator.Bind(syntax.OperatorToken.Kind, boundOperand.Type);

        if (boundOperator == null)
        {
            _diagnostics.ReportUndefinedUnaryOperator(syntax.OperatorToken.Location, syntax.OperatorToken.Text, boundOperand.Type);
            return new BoundErrorExpression();
        }

        return new BoundUnaryExpression(boundOperator, boundOperand);
    }

    private BoundExpression BindLiteralExpression(LiteralExpressionSyntax syntax)
    {
        // Tested on the token rather than on a null Value, so that a malformed literal whose value
        // failed to parse cannot be mistaken for someone having written `null`. This used to read
        // `syntax.Value ?? 0`, which bound every `null` in the language to the integer 0.
        if (syntax.LiteralToken.Kind == SyntaxKind.NullKeyword)
        {
            return new BoundNullExpression();
        }

        var value = syntax.Value ?? 0;
        return new BoundLiteralExpression(value);
    }

    private BoundExpression BindNameExpression(NameExpressionSyntax syntax)
    {
        var name = syntax.IdentifierToken.Text;

        if (syntax.IdentifierToken.IsMissing)
        {
            return new BoundErrorExpression();
        }

        if (!_scope.TryLookupVariable(name, out var variable))
        {
            // A bare name that is not a variable but is a function is that function used as a
            // value. Purely additive: before function values this was always "does not exist".
            if (_scope.TryLookupFunction(name, out var function) && function != null)
            {
                return BindFunctionReference(function, syntax.IdentifierToken.Location);
            }

            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Location, name);
            return new BoundErrorExpression();
        }

        _recorder?.RecordSymbol(syntax.IdentifierToken.Location, variable, OccurrenceKind.Reference);

        return new BoundVariableExpression(variable!);
    }

    /// <summary>
    /// Turns a named function into a value of its own signature's type.
    /// </summary>
    /// <remarks>
    /// A generic function has no single signature to take a reference to, and a .NET method is not
    /// something the backends can produce a plain function pointer for, so both are refused here
    /// rather than producing something only the .NET backend could emit.
    /// </remarks>
    private BoundExpression BindFunctionReference(FunctionSymbol function, TextLocation location)
    {
        if (function.IsGeneric || function is DotNetFunctionSymbol)
        {
            _diagnostics.ReportCannotUseAsFunctionValue(location, function.Name);
            return new BoundErrorExpression();
        }

        _recorder?.RecordSymbol(location, function, OccurrenceKind.Reference);

        var parameterTypes = function.Parameters.Select(p => p.Type).ToImmutableArray();
        var functionType = new FunctionTypeSymbol(parameterTypes, function.Type);

        return new BoundFunctionReference(function, functionType);
    }

    private BoundExpression BindCallExpression(CallExpressionSyntax syntax, TypeSymbol? expectedType = null)
    {
        // Separate `name: value` arguments from positional ones. Everything below works on the
        // unwrapped expressions, so nothing downstream of this method sees a NamedArgumentSyntax.
        var argumentSyntaxes = new ExpressionSyntax[syntax.Arguments.Count];
        var argumentNames = new SyntaxToken?[syntax.Arguments.Count];
        var namedArgumentCount = 0;

        for (int i = 0; i < syntax.Arguments.Count; i++)
        {
            if (syntax.Arguments[i] is NamedArgumentSyntax named)
            {
                argumentSyntaxes[i] = named.Expression;
                argumentNames[i] = named.Identifier;
                namedArgumentCount++;
            }
            else
            {
                argumentSyntaxes[i] = syntax.Arguments[i];

                if (namedArgumentCount > 0)
                {
                    _diagnostics.ReportPositionalArgumentAfterNamed(syntax.Arguments[i].Location);
                    return new BoundErrorExpression();
                }
            }
        }

        // Special case: array_new(size) creates a zero-initialized fixed-length array
        if (syntax.Identifier.Text == "array_new" && syntax.Arguments.Count == 1 && namedArgumentCount == 0)
        {
            var sizeArg = BindExpression(argumentSyntaxes[0]);
            sizeArg = BindConversion(argumentSyntaxes[0].Location, sizeArg, TypeSymbol.Int);
            var elementType = TypeSymbol.Any;
            if (expectedType != null && expectedType.Name == "array" && expectedType.TypeArguments.Length == 1)
                elementType = expectedType.TypeArguments[0];
            // Also check explicit type argument on the call: array_new<int>(8)
            if (elementType == TypeSymbol.Any && syntax.TypeArguments.Length == 1)
                elementType = BindTypeSyntax(syntax.TypeArguments[0]);
            return new BoundArrayNewExpression(elementType, sizeArg);
        }

        if (syntax.Arguments.Count == 1
            && namedArgumentCount == 0
            && !_scope.TryLookupFunction(syntax.Identifier.Text, out _)
            && LookupType(syntax.Identifier.Text) is TypeSymbol type)
        {
            return BindConversion(argumentSyntaxes[0], type, true);
        }

        // Calling a variable or parameter that holds a function value. Checked before the function
        // table so that a handler named after the function it defaults to still calls the value it
        // was given, not the function that shares its name.
        if (_scope.TryLookupVariable(syntax.Identifier.Text, out var callee)
            && callee?.Type is FunctionTypeSymbol calleeType)
        {
            return BindIndirectCall(syntax, argumentSyntaxes, argumentNames, namedArgumentCount,
                callee, calleeType);
        }

        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var argument in argumentSyntaxes)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }

        if (!_scope.TryLookupFunction(syntax.Identifier.Text, out var function))
        {
            var dotNetFunc = TryResolveDotNetFunction(syntax.Identifier.Text, boundArguments.Count);
            if (dotNetFunc != null)
            {
                function = dotNetFunc;
            }
            else
            {
                _diagnostics.ReportUndefinedFunction(syntax.Identifier.Location, syntax.Identifier.Text);
                return new BoundErrorExpression();
            }
        }

        // Recorded before generic instantiation replaces the symbol, so the occurrence names the
        // function as it is written in the source rather than DynArray_push<int>.
        _recorder?.RecordSymbol(syntax.Identifier.Location, function, OccurrenceKind.Reference);

        // Named arguments are a ProLang-function feature. Interop overloads are picked by metadata
        // signature, where a parameter's name is not part of the contract it publishes.
        if (namedArgumentCount > 0 && function is DotNetFunctionSymbol)
        {
            _diagnostics.ReportNamedArgumentNotSupported(syntax.Identifier.Location, function.Name);
            return new BoundErrorExpression();
        }

        // Put the arguments in parameter order and fill in the defaults, so that everything past
        // this point — generic inference included — sees a complete, positional argument list.
        if (!TryReorderArguments(syntax, function!, argumentNames, boundArguments,
                out var orderedArguments, out var orderedLocations))
        {
            return new BoundErrorExpression();
        }

        // Generic function instantiation
        if (function.IsGeneric)
        {
            TypeSymbol[] typeArgs;
            if (syntax.TypeArguments.Length > 0)
            {
                typeArgs = syntax.TypeArguments.Select(t => BindTypeSyntax(t)).ToArray();
            }
            else
            {
                typeArgs = InferTypeArguments(function, orderedArguments.ToImmutable());
            }

            if (typeArgs.Length != function.TypeParameters.Length)
            {
                _diagnostics.ReportWrongArgumentCount(syntax.Location, function.Name,
                    function.TypeParameters.Length, typeArgs.Length);
                return new BoundErrorExpression();
            }

            function = function.InstantiateGeneric(typeArgs);
            // Reuse cached symbol if already instantiated (ensures reference equality for emitter)
            if (_sharedInstantiations != null && _sharedInstantiations.TryGetValue(function.Name, out var cached) && cached.Symbol != null)
                function = cached.Symbol;
            else
                EnsureInstantiated(function);
        }

        // Instantiation substitutes types but preserves arity and order, so the ordered list built
        // above still lines up with the parameters here.
        for (int i = 0; i < orderedArguments.Count; i++)
        {
            orderedArguments[i] = BindConversion(orderedLocations[i], orderedArguments[i],
                function.Parameters[i].Type);
        }

        return new BoundCallExpression(function, orderedArguments.ToImmutable());
    }

    /// <summary>
    /// Binds a call through a function value.
    /// </summary>
    /// <remarks>
    /// A function type records parameter types but not their names or defaults — a value can be
    /// assigned from any function with a matching signature, and two of those may disagree about
    /// both. Named arguments and omitted parameters are therefore only available when calling a
    /// function by name.
    /// </remarks>
    private BoundExpression BindIndirectCall(
        CallExpressionSyntax syntax,
        ExpressionSyntax[] argumentSyntaxes,
        SyntaxToken?[] argumentNames,
        int namedArgumentCount,
        VariableSymbol callee,
        FunctionTypeSymbol calleeType)
    {
        if (namedArgumentCount > 0)
        {
            var firstName = argumentNames.First(n => n != null)!;
            _diagnostics.ReportNamedArgumentThroughFunctionValue(firstName.Location, callee.Name);
            return new BoundErrorExpression();
        }

        if (argumentSyntaxes.Length != calleeType.ParameterTypes.Length)
        {
            _diagnostics.ReportWrongArgumentCount(syntax.Location, callee.Name,
                calleeType.ParameterTypes.Length, argumentSyntaxes.Length);
            return new BoundErrorExpression();
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(argumentSyntaxes.Length);

        for (int i = 0; i < argumentSyntaxes.Length; i++)
        {
            var bound = BindExpression(argumentSyntaxes[i]);
            arguments.Add(BindConversion(argumentSyntaxes[i].Location, bound, calleeType.ParameterTypes[i]));
        }

        return new BoundIndirectCallExpression(new BoundVariableExpression(callee), calleeType,
            arguments.ToImmutable());
    }

    /// <summary>
    /// Maps the arguments as written onto the parameter list, filling omitted optional parameters
    /// with their defaults.
    /// </summary>
    /// <remarks>
    /// Positional arguments are known to be a prefix of the list — <c>BindCallExpression</c> rejects
    /// a positional argument after a named one — so a positional argument's index is its ordinal.
    /// </remarks>
    private bool TryReorderArguments(
        CallExpressionSyntax syntax,
        FunctionSymbol function,
        SyntaxToken?[] argumentNames,
        ImmutableArray<BoundExpression>.Builder boundArguments,
        out ImmutableArray<BoundExpression>.Builder ordered,
        out TextLocation[] locations)
    {
        var parameters = function.Parameters;
        var slots = new BoundExpression?[parameters.Length];

        ordered = ImmutableArray.CreateBuilder<BoundExpression>(parameters.Length);
        locations = new TextLocation[parameters.Length];

        for (int i = 0; i < boundArguments.Count; i++)
        {
            var location = syntax.Arguments[i].Location;
            var name = argumentNames[i];
            int target;

            if (name == null)
            {
                if (i >= parameters.Length)
                {
                    _diagnostics.ReportWrongArgumentCount(syntax.Location, function.Name,
                        parameters.Length, boundArguments.Count);
                    return false;
                }

                target = i;
            }
            else
            {
                target = -1;

                for (int p = 0; p < parameters.Length; p++)
                {
                    if (parameters[p].Name == name.Text)
                    {
                        target = p;
                        break;
                    }
                }

                if (target < 0)
                {
                    _diagnostics.ReportUndefinedArgumentName(name.Location, function.Name, name.Text);
                    return false;
                }
            }

            if (slots[target] != null)
            {
                _diagnostics.ReportArgumentAlreadyGiven(location, parameters[target].Name);
                return false;
            }

            slots[target] = boundArguments[i];
            locations[target] = location;
        }

        for (int p = 0; p < parameters.Length; p++)
        {
            if (slots[p] != null)
            {
                continue;
            }

            var parameter = parameters[p];

            if (!parameter.IsOptional)
            {
                _diagnostics.ReportMissingRequiredArgument(syntax.Location, function.Name, parameter.Name);
                return false;
            }

            // A fresh literal per call site: the default is a constant, so there is nothing to
            // re-bind and no expression shared between two calls.
            slots[p] = new BoundLiteralExpression(parameter.DefaultValue!);
            locations[p] = syntax.Location;
        }

        foreach (var slot in slots)
        {
            ordered.Add(slot!);
        }

        return true;
    }

    private TypeSymbol[] InferTypeArguments(FunctionSymbol generic, ImmutableArray<BoundExpression> args)
    {
        // Build substitution by matching arg types against param types
        var result = new TypeSymbol[generic.TypeParameters.Length];
        for (int i = 0; i < generic.Parameters.Length && i < args.Length; i++)
        {
            ExtractTypeArgs(generic.Parameters[i].Type, args[i].Type, generic.TypeParameters, result);
        }
        // Fill any unresolved with Any
        for (int i = 0; i < result.Length; i++)
            result[i] ??= TypeSymbol.Any;
        return result;
    }

    private static void ExtractTypeArgs(TypeSymbol paramType, TypeSymbol argType,
        ImmutableArray<TypeParameterSymbol> typeParams, TypeSymbol[] result)
    {
        if (paramType is TypeParameterSymbol tp)
        {
            var idx = tp.Index;
            if (idx < result.Length && result[idx] == null)
                result[idx] = argType;
            return;
        }
        // Recurse into type arguments (e.g., DynArray<T> matched against DynArray<int>)
        if (paramType.TypeArguments.Length == argType.TypeArguments.Length)
        {
            for (int i = 0; i < paramType.TypeArguments.Length; i++)
                ExtractTypeArgs(paramType.TypeArguments[i], argType.TypeArguments[i], typeParams, result);
        }
    }

    private void EnsureInstantiated(FunctionSymbol concrete)
    {
        if (_sharedInstantiations == null) return;
        if (_sharedInstantiations.ContainsKey(concrete.Name)) return;

        var generic = concrete.OriginalGeneric;
        if (generic?.Declaration == null) return;

        // Reserve the slot to prevent re-entrant binding of the same instantiation
        _sharedInstantiations[concrete.Name] = default;

        var typeBindings = generic.TypeParameters
            .Zip(concrete.TypeArguments)
            .ToDictionary(p => p.First.Name, p => p.Second);

        var instBinder = new Binder(_isScript, _scope!, concrete, typeBindings, _sharedInstantiations);
        var body = instBinder.BindStatement(generic.Declaration.Body);
        var lowered = Lowerer.Lower(body);
        _sharedInstantiations[concrete.Name] = (concrete, lowered);
        _diagnostics.AddRange(instBinder.Diagnostics);
    }

    /// <summary>
    /// Tries to resolve a function from .NET assemblies by searching for static methods.
    /// </summary>
    /// <summary>
    /// Last resort for an unqualified call: a public static method of that exact name and arity,
    /// somewhere in the loaded assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to match <b>case-insensitively</b> and take the <b>first method with the right
    /// name regardless of how many parameters it had</b>, across every loaded assembly in
    /// whatever order reflection produced them. A misspelled call did not report an undefined
    /// function — it silently bound to something in the BCL and then failed with an argument
    /// count complaint about a method the program had never heard of. <c>equals(1)</c> reported
    /// that <c>Equals</c> requires 2 arguments; <c>exit(0)</c> reported that <c>Exit</c> requires
    /// 0. Which method won also depended on assembly load order, so the same source could
    /// diagnose differently depending on what had been imported.
    /// </para>
    /// <para>
    /// Now the name must match exactly — ProLang is case-sensitive everywhere else — the
    /// parameter count must match the call, and a name that resolves to methods on more than one
    /// type is refused rather than picked between. Refusing means the caller reports the
    /// undefined function it actually is, which is what a typo should produce.
    /// </para>
    /// </remarks>
    /// <param name="name">The identifier at the call site.</param>
    /// <param name="argumentCount">How many arguments the call passes.</param>
    private DotNetFunctionSymbol? TryResolveDotNetFunction(string name, int argumentCount)
    {
        var registry = DotNetAssemblyRegistry.Instance;

        MethodInfo? match = null;

        // Ordered so that the outcome does not depend on the order reflection happens to return
        // assemblies and types in, which varies with what the program imported.
        foreach (var assembly in registry.GetLoadedAssemblies().OrderBy(a => a.FullName, StringComparer.Ordinal))
        {
            try
            {
                foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    if (!type.IsPublic)
                    {
                        continue;
                    }

                    foreach (var method in registry.GetStaticMethods(type))
                    {
                        if (!string.Equals(method.Name, name, StringComparison.Ordinal)
                            || method.GetParameters().Length != argumentCount)
                        {
                            continue;
                        }

                        if (match == null)
                        {
                            match = method;
                            continue;
                        }

                        // Overloads of the same arity on the same type are already resolved
                        // elsewhere by metadata order; two different types is a genuine
                        // ambiguity that this has no basis for deciding.
                        if (match.DeclaringType != method.DeclaringType)
                        {
                            return null;
                        }
                    }
                }
            }
            catch (Exception e) when (e is ReflectionTypeLoadException or FileNotFoundException or TypeLoadException)
            {
                // An assembly whose types cannot be loaded contributes nothing and is not an
                // error in the program being compiled.
            }
        }

        return match == null ? null : DotNetFunctionSymbol.FromStaticMethod(match);
    }

    private static readonly Dictionary<string, FunctionSymbol> ArrayMethods = new(StringComparer.Ordinal)
    {
        { "length", BuiltInFunctions.ArrayLength },
    };

    private static readonly HashSet<string> _dynamicArrayMethods = new(StringComparer.Ordinal)
        { "push", "pop", "getAt" };

    private static readonly Dictionary<string, FunctionSymbol> StringMethods = new(StringComparer.Ordinal)
    {
        { "length", BuiltInFunctions.StringLength },
        { "charAt", BuiltInFunctions.StringCharAt },
        { "charCode", BuiltInFunctions.StringCharCode },
        { "substring", BuiltInFunctions.StringSubstring },
        { "indexOf", BuiltInFunctions.StringIndexOf },
    };

    private BoundExpression BindMethodCallExpression(MethodCallExpressionSyntax syntax)
    {
        var methodName = syntax.MethodName.Text;

        // First check if the expression is a NameExpression that could be a .NET type name.
        // Only attempt .NET type resolution when the name is NOT a declared variable —
        // variable names (json, arr, pos, …) are never .NET type names, and scanning all
        // loaded assemblies for every one of them is the primary compilation bottleneck.
        if (syntax.Expression is NameExpressionSyntax nameExpr)
        {
            var typeName = nameExpr.IdentifierToken.Text;

            if (!_scope.TryLookupVariable(typeName, out _))
            {
                // Construction is written Type.new(args), matching how the interop module
                // registers constructors.
                if (methodName == "new")
                {
                    var constructor = ResolveDotNetConstructor(typeName, syntax.Arguments.Count);

                    if (constructor != null)
                    {
                        return BindDotNetFunctionCall(syntax, constructor, syntax.Arguments);
                    }
                }

                var dotNetFunc = ResolveDotNetStaticMethod(typeName, methodName, syntax.Arguments.Count);
                if (dotNetFunc != null)
                {
                    return BindDotNetFunctionCall(syntax, dotNetFunc, syntax.Arguments);
                }
            }
        }

        // Otherwise, bind the expression normally
        var receiver = BindExpression(syntax.Expression);

        if (receiver.Type == TypeSymbol.Error)
        {
            return new BoundErrorExpression();
        }

        // An instance call on a value whose .NET type is known. Values used to be typed `any`,
        // which erased the type and left nothing to resolve the member against.
        if (receiver.Type is DotNetTypeSymbol dotNetReceiver)
        {
            // A value-type receiver needs its *address* for the call, which the bound tree has no
            // way to express — IL's `constrained.` prefix exists precisely for this. Boxing it
            // instead produces IL that verifies and then reads the object header as if it were
            // the struct's data, silently returning wrong answers. Refusing is the honest
            // outcome until an address-of node exists.
            if (dotNetReceiver.ClrType.IsValueType)
            {
                _diagnostics.ReportValueTypeInstanceCallUnsupported(
                    syntax.MethodName.Location, methodName, dotNetReceiver.Name);

                return new BoundErrorExpression();
            }

            var instanceMethod = ResolveDotNetInstanceMethod(dotNetReceiver.ClrType, methodName, syntax.Arguments.Count);

            if (instanceMethod != null)
            {
                return BindDotNetInstanceCall(syntax, receiver, instanceMethod, syntax.Arguments);
            }

            _diagnostics.ReportUndefinedMethod(syntax.MethodName.Location, methodName, receiver.Type);
            return new BoundErrorExpression();
        }

        // Handle array methods
        if (receiver.Type.Name == "array")
        {
            if (_dynamicArrayMethods.Contains(methodName))
            {
                _diagnostics.ReportMethodNotAvailableOnFixedArray(syntax.MethodName.Location, methodName);
                return new BoundErrorExpression();
            }

            if (!ArrayMethods.TryGetValue(methodName, out var function))
            {
                _diagnostics.ReportUndefinedMethod(syntax.MethodName.Location, methodName, receiver.Type);
                return new BoundErrorExpression();
            }

            var expectedArgCount = function.Parameters.Length - 1;

            if (syntax.Arguments.Count != expectedArgCount)
            {
                _diagnostics.ReportWrongMethodArgumentCount(syntax.Location, methodName, expectedArgCount, syntax.Arguments.Count);
                return new BoundErrorExpression();
            }

            var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

            var receiverParam = function.Parameters[0];
            boundArguments.Add(BindConversion(syntax.Expression.Location, receiver, receiverParam.Type));

            for (int i = 0; i < syntax.Arguments.Count; i++)
            {
                var argument = BindExpression(syntax.Arguments[i]);
                var parameter = function.Parameters[i + 1];
                boundArguments.Add(BindConversion(syntax.Arguments[i].Location, argument, parameter.Type));
            }

            _recorder?.RecordSymbol(syntax.MethodName.Location, function, OccurrenceKind.Reference);

            return new BoundCallExpression(function, boundArguments.ToImmutable());
        }

        // Handle string methods
        if (receiver.Type == TypeSymbol.String)
        {
            if (!StringMethods.TryGetValue(methodName, out var function))
            {
                _diagnostics.ReportUndefinedMethod(syntax.MethodName.Location, methodName, receiver.Type);
                return new BoundErrorExpression();
            }

            var expectedArgCount = function.Parameters.Length - 1;

            if (syntax.Arguments.Count != expectedArgCount)
            {
                _diagnostics.ReportWrongMethodArgumentCount(syntax.Location, methodName, expectedArgCount, syntax.Arguments.Count);
                return new BoundErrorExpression();
            }

            var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

            var receiverParam = function.Parameters[0];
            boundArguments.Add(BindConversion(syntax.Expression.Location, receiver, receiverParam.Type));

            for (int i = 0; i < syntax.Arguments.Count; i++)
            {
                var argument = BindExpression(syntax.Arguments[i]);
                var parameter = function.Parameters[i + 1];
                boundArguments.Add(BindConversion(syntax.Arguments[i].Location, argument, parameter.Type));
            }

            _recorder?.RecordSymbol(syntax.MethodName.Location, function, OccurrenceKind.Reference);

            return new BoundCallExpression(function, boundArguments.ToImmutable());
        }

        // Handle qualified function names registered in scope (e.g., "Math.Max").
        // Do NOT call ResolveDotNetStaticMethod here — the receiver is already a bound
        // variable, so it is definitely not a .NET type name.
        if (receiver is BoundVariableExpression varExpr)
        {
            var qualifiedName = $"{varExpr.Variable.Name}.{methodName}";
            if (_scope.TryLookupFunction(qualifiedName, out var func))
            {
                return BindFunctionCall(syntax, func!, syntax.Arguments);
            }
        }

        // A receiver typed `any` has genuinely lost its .NET type — it came from a cast, a
        // map<string, any>, or a parameter declared `any` — so there is nothing to resolve the
        // member against. Values that came directly from .NET keep their type and are handled
        // above as DotNetTypeSymbol.
        _diagnostics.ReportUndefinedMethod(syntax.MethodName.Location, methodName, receiver.Type);
        return new BoundErrorExpression();
    }

    /// <summary>
    /// Resolves a .NET static method by type name and method name.
    /// </summary>
    private DotNetFunctionSymbol? ResolveDotNetStaticMethod(string typeName, string methodName, int argumentCount)
    {
        var registry = DotNetAssemblyRegistry.Instance;

        // Try to find the type by full name first
        Type? dotNetType = registry.FindType(typeName);

        if (dotNetType == null)
        {
            dotNetType = registry.FindTypeBySimpleName(typeName);
        }

        if (dotNetType == null)
        {
            // Try common namespace prefixes as fallback
            foreach (var ns in new[] { "System", "System.Collections.Generic", "System.Text", "System.IO", "System.Linq" })
            {
                dotNetType = registry.FindTypeByNamespace(ns, typeName);
                if (dotNetType != null) break;
            }
        }

        if (dotNetType == null)
            return null;

        // First try to find a static method. Arity is part of the match: String.Concat has
        // overloads from one to four arguments, and picking whichever came first in metadata
        // order made `String.Concat("a", "b")` fail with "requires 1 arguments but was given 2".
        var methods = registry.GetStaticMethods(dotNetType)
            .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matchingMethod = methods.FirstOrDefault(m => m.GetParameters().Length == argumentCount)
            // No overload of the right arity — fall back to any, so the diagnostic reports the
            // argument-count mismatch against a real signature rather than "method not found".
            ?? methods.FirstOrDefault();

        if (matchingMethod != null)
            return DotNetFunctionSymbol.FromStaticMethod(matchingMethod);

        // If no method found, try to find a static property
        var properties = registry.GetStaticProperties(dotNetType);
        var matchingProperty = properties.FirstOrDefault(p =>
            p.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

        if (matchingProperty != null)
            return DotNetFunctionSymbol.FromStaticProperty(matchingProperty);

        // If still not found, try to find a static field (constants, etc.)
        var fields = dotNetType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var matchingField = fields.FirstOrDefault(f =>
            f.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

        if (matchingField != null)
            return DotNetFunctionSymbol.FromStaticField(matchingField);

        return null;
    }

    /// <summary>
    /// Resolves a constructor for <paramref name="typeName"/> taking <paramref name="argumentCount"/> arguments.
    /// </summary>
    /// <remarks>
    /// Matching is by arity. Overloaded constructors differing only in parameter types resolve to
    /// whichever comes first, the same limitation the emitter has when converting a reflection
    /// member into a Cecil reference.
    /// </remarks>
    private static DotNetFunctionSymbol? ResolveDotNetConstructor(string typeName, int argumentCount)
    {
        var type = FindDotNetType(typeName);

        if (type == null)
        {
            return null;
        }

        var constructor = DotNetAssemblyRegistry.GetConstructors(type)
            .FirstOrDefault(c => c.GetParameters().Length == argumentCount);

        return constructor == null ? null : DotNetFunctionSymbol.FromConstructor(constructor);
    }

    /// <summary>
    /// Resolves an instance method on a known .NET type.
    /// </summary>
    /// <remarks>
    /// This used to return null unconditionally, with a comment that the type could not be known
    /// at bind time — true while every .NET value was typed <c>any</c>, but no longer so now that
    /// <see cref="DotNetTypeSymbol"/> carries it. Inherited members are included, so
    /// <c>ToString</c> and friends resolve.
    /// </remarks>
    private static DotNetFunctionSymbol? ResolveDotNetInstanceMethod(Type receiverType, string methodName, int argumentCount)
    {
        var method = receiverType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase)
                && m.GetParameters().Length == argumentCount);

        if (method != null)
        {
            return DotNetFunctionSymbol.FromInstanceMethod(method);
        }

        // A property read written as a call, e.g. sb.Length().
        var property = receiverType
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(p => p.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) && p.GetMethod != null);

        return property == null || argumentCount != 0
            ? null
            : DotNetFunctionSymbol.FromInstanceMethod(property.GetMethod!);
    }

    /// <summary>
    /// Finds a .NET type by simple or full name, trying the namespaces programs commonly use.
    /// </summary>
    private static Type? FindDotNetType(string typeName)
    {
        var registry = DotNetAssemblyRegistry.Instance;
        var type = registry.FindType(typeName) ?? registry.FindTypeBySimpleName(typeName);

        if (type != null)
        {
            return type;
        }

        foreach (var ns in new[] { "System", "System.Collections.Generic", "System.Text", "System.IO", "System.Linq" })
        {
            type = registry.FindTypeByNamespace(ns, typeName);

            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Binds a call to a .NET function.
    /// </summary>
    /// <summary>
    /// Binds an instance call on a value whose .NET type is known.
    /// </summary>
    /// <remarks>
    /// The receiver becomes the first argument. <c>callvirt</c> expects it on the stack below the
    /// arguments, and the emitter pushes a call's arguments in order, so putting it first there
    /// produces exactly the right sequence without the emitter needing a separate notion of a
    /// receiver.
    /// </remarks>
    private BoundExpression BindDotNetInstanceCall(
        MethodCallExpressionSyntax syntax,
        BoundExpression receiver,
        DotNetFunctionSymbol method,
        SeparatedSyntaxList<ExpressionSyntax> arguments)
    {
        if (arguments.Count != method.Parameters.Length)
        {
            _diagnostics.ReportWrongMethodArgumentCount(
                syntax.MethodName.Location, method.Name, method.Parameters.Length, arguments.Count);

            return new BoundErrorExpression();
        }

        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Count + 1);
        boundArguments.Add(receiver);

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = BindExpression(arguments[i]);
            boundArguments.Add(BindConversion(arguments[i].Location, argument, method.Parameters[i].Type));
        }

        return new BoundCallExpression(method, boundArguments.ToImmutable());
    }

    private BoundExpression BindDotNetFunctionCall(
        MethodCallExpressionSyntax syntax,
        DotNetFunctionSymbol function,
        SeparatedSyntaxList<ExpressionSyntax> arguments)
    {
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var argument in arguments)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }

        if (arguments.Count != function.Parameters.Length)
        {
            _diagnostics.ReportWrongArgumentCount(syntax.Location, function.Name, function.Parameters.Length,
                arguments.Count);
            return new BoundErrorExpression();
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            var argumentLocation = arguments[i].Location;
            var argument = boundArguments[i];
            var parameter = function.Parameters[i];

            boundArguments[i] = BindConversion(argumentLocation, argument, parameter.Type);
        }

        return new BoundCallExpression(function, boundArguments.ToImmutable());
    }

    /// <summary>
    /// Binds a .NET instance method call.
    /// </summary>
    private BoundExpression BindDotNetInstanceMethodCall(
        MethodCallExpressionSyntax syntax,
        BoundExpression receiver,
        DotNetFunctionSymbol function,
        SeparatedSyntaxList<ExpressionSyntax> arguments)
    {
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

        // First argument is the receiver (the instance)
        boundArguments.Add(receiver);

        foreach (var argument in arguments)
        {
            boundArguments.Add(BindExpression(argument));
        }

        return new BoundCallExpression(function, boundArguments.ToImmutable());
    }

    /// <summary>
    /// Binds a function call by function symbol.
    /// </summary>
    private BoundExpression BindFunctionCall(
        MethodCallExpressionSyntax syntax,
        FunctionSymbol function,
        SeparatedSyntaxList<ExpressionSyntax> arguments)
    {
        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var argument in arguments)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }

        if (arguments.Count != function.Parameters.Length)
        {
            _diagnostics.ReportWrongArgumentCount(syntax.Location, function.Name, function.Parameters.Length,
                arguments.Count);
            return new BoundErrorExpression();
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            var argumentLocation = arguments[i].Location;
            var argument = boundArguments[i];
            var parameter = function.Parameters[i];

            boundArguments[i] = BindConversion(argumentLocation, argument, parameter.Type);
        }

        return new BoundCallExpression(function, boundArguments.ToImmutable());
    }

    private BoundExpression BindConversion(ExpressionSyntax syntax, TypeSymbol type, bool allowExplicit = false)
    {
        var expression = BindExpression(syntax, expectedType: type);

        return BindConversion(syntax.Location, expression, type, allowExplicit);
    }

    private BoundExpression BindConversion(TextLocation diagnosticLocation, BoundExpression expression, TypeSymbol type, bool allowExplicit = false)
    {
        //this is for values that can fit into 32-bit integer
        if (expression is BoundLiteralExpression intLiteral && intLiteral.Value is int)
        {
            var v = Convert.ToInt32(intLiteral.Value);

            if (type == TypeSymbol.Int8 && v >= sbyte.MinValue  && v <= sbyte.MaxValue) 
            {
                return new BoundLiteralExpression((sbyte)v);
            }

            if (type == TypeSymbol.UInt8  && v >= byte.MinValue && v <= byte.MaxValue)  
            { 
                return new BoundLiteralExpression((byte)v);
            }

            if (type == TypeSymbol.Int16  && v >= short.MinValue  && v <= short.MaxValue)
            {
                 return new BoundLiteralExpression((short)v);
            }

            if (type == TypeSymbol.UInt16 && v >= ushort.MinValue && v <= ushort.MaxValue) 
            {
                return new BoundLiteralExpression((ushort)v);
            }

            if (type == TypeSymbol.UInt64 && (ulong)v >= ulong.MinValue && (ulong)v <= ulong.MaxValue) 
            {
                return new BoundLiteralExpression((ulong)v);
            }

            if (type == TypeSymbol.Int64) 
            {
                return new BoundLiteralExpression((long)v);
            }
        }

        // Smart coercion for float64 literals assigned to float32/float targets
        if (expression is BoundLiteralExpression dblLit && dblLit.Value is double dv)
        {
            if (type == TypeSymbol.Float32)
                return new BoundLiteralExpression((float)dv);
            if (type == TypeSymbol.Float || type == TypeSymbol.Float64)
                return new BoundLiteralExpression(dv);
        }

        // Smart coercion for float32 literals assigned to float64/float targets
        if (expression is BoundLiteralExpression fltLit && fltLit.Value is float fv)
        {
            if (type == TypeSymbol.Float64 || type == TypeSymbol.Float)
                return new BoundLiteralExpression((double)fv);
            if (type == TypeSymbol.Float32)
                return new BoundLiteralExpression(fv);
        }

        if (expression.Type == type){
            return expression;
        }

        // Conversions to and from `any` are always allowed without an explicit cast, because
        // `any` carries no information to lose. A .NET value is System.Object at the IL level and
        // was itself typed `any` until DotNetTypeSymbol existed, so it takes the same path —
        // otherwise giving those values their real type would silently start rejecting programs
        // that compiled before, such as `let g: string = Guid.NewGuid()`.
        if (type == TypeSymbol.Any || type is DotNetTypeSymbol)
        {
            return new BoundConversionExpression(type, expression);
        }

        if (expression.Type == TypeSymbol.Any || expression.Type is DotNetTypeSymbol)
        {
            return new BoundConversionExpression(type, expression);
        }

        var conversion = Conversion.Classify(expression.Type, type);

        if (!conversion.Exists)
        {
            if (expression.Type != TypeSymbol.Error && type != TypeSymbol.Error)
            {
                _diagnostics.ReportCannotConvert(diagnosticLocation, expression.Type, type);
            }

            return new BoundErrorExpression();
        }

        if (!allowExplicit && conversion.IsExplicit)
        {
            _diagnostics.ReportCannotConvertImplicitly(diagnosticLocation, expression.Type, type);
        }

        if (conversion.IsIdentity)
        {
            return expression;
        }

        return new BoundConversionExpression(type, expression);
    }

    /// <summary>
    /// The comment written above a declaration, as its documentation.
    /// </summary>
    /// <remarks>
    /// Done for every compile rather than only when tooling asks, because it costs a short
    /// backwards scan per declaration and it is what lets everything downstream read documentation
    /// from one place — <see cref="Symbol.Documentation"/> — without knowing or caring whether the
    /// symbol came from a <c>.prl</c> file or from the builtin table.
    /// </remarks>
    private static string? DocumentationFor(DeclarationSyntax syntax) =>
        DocumentationExtractor.ForDeclaration(syntax.SyntaxTree.Text, syntax.Span.Start);

    private VariableSymbol BindVariable(SyntaxToken identifier, bool isReadonly, TypeSymbol type)
    {
        var name = identifier.Text ?? "?";
        var declare = !identifier.IsMissing;
        var variable = _function == null ?
            (VariableSymbol)new GlobalVariableSymbol(name, isReadonly, type) :
            new LocalVariableSymbol(name, isReadonly, type);

        if (declare && !_scope.TryDeclareVariable(variable))
        {
            // Variable already declared — try to look up the existing one
            if (_scope.TryLookupVariable(name, out var existing))
            {
                // Recorded against the symbol that survived, so that both spellings of the name
                // belong to the same symbol and rename reaches both.
                _recorder?.RecordSymbol(identifier.Location, existing, OccurrenceKind.Definition);

                return existing!;
            }
            _diagnostics.ReportVariableAlreadyDeclared(identifier.Location, name);
        }

        _recorder?.RecordSymbol(identifier.Location, variable, OccurrenceKind.Definition);

        return variable;
    }
}