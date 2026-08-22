using System.Collections.Immutable;
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

    public Binder(bool isScript, BoundScope parent, FunctionSymbol? function,
        Dictionary<string, TypeSymbol>? typeBindings = null,
        Dictionary<string, (FunctionSymbol Symbol, BoundBlockStatement Body)>? sharedInstantiations = null)
    {
        _scope = new BoundScope(parent);
        _isScript = isScript;
        _function = function;
        _sharedInstantiations = sharedInstantiations;

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

    public static BoundGlobalScope BindGlobalScope(bool isScript, BoundGlobalScope? previous, ImmutableArray<SyntaxTree> syntaxTrees, ImmutableHashSet<string>? importedModules = null)
    {
        var parentScope = CreateParentScope(previous, importedModules);

        var binder = new Binder(isScript, parentScope, null);

        // Single-pass collection of all declarations to avoid multiple SelectMany iterations
        var allDeclarations = syntaxTrees.SelectMany(st => st.Root.Declarations).ToList();
        var structDeclarations = allDeclarations.OfType<StructDeclarationSyntax>();
        var enumDeclarations = allDeclarations.OfType<EnumDeclarationSyntax>();
        var functionDeclarations = allDeclarations.OfType<FunctionDeclarationSyntax>();
        var globalStatements = allDeclarations.OfType<GlobalStatementSyntax>();

        foreach (var enumDecl in enumDeclarations)
        {
            binder.BindEnumDeclaration(enumDecl);
        }

        foreach (var structDecl in structDeclarations)
        {
            binder.BindStructDeclaration(structDecl);
        }

        foreach (var function in functionDeclarations)
        {
            binder.BindFunctionDeclaration(function);
        }

        // Pre-register global variables from all files so they are visible
        // regardless of statement processing order (important for imports)
        binder.RegisterGlobalVariables(syntaxTrees);

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
                binder._scope = new BoundScope(binder._scope.Parent);
                foreach (var fn in initialFunctions)
                {
                    if (fn.Name != "main")
                    {
                        binder._scope.TryDeclareFunction(fn);
                    }
                }
                binder._scope.TryDeclareFunction(userMainFunction);

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

        return new BoundGlobalScope(previous, diagnostics, finalMainFunction, scriptFunction, functions, variables, statements.ToImmutableArray(), structTypes, importedModules, enumTypes);
    }

    public static BoundProgram BindProgram(bool isScript, BoundProgram previous, BoundGlobalScope? globalScope)
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
                continue;

            var binder = new Binder(isScript, parentScope, function,
                sharedInstantiations: sharedInstantiations);

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

        // Add all collected generic instantiations to the function bodies
        foreach (var (_, (concreteSymbol, instBody)) in sharedInstantiations)
        {
            if (instBody != null && !functionBodies.ContainsKey(concreteSymbol))
                functionBodies.Add(concreteSymbol, instBody);
        }

        return new BoundProgram(previous, diagnostics.ToImmutable(), globalScope.MainFunction, globalScope.ScriptFunction, functionBodies.ToImmutable(), globalScope.StructTypes, globalScope.EnumTypes, structuredBodies.ToImmutable());
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

        foreach (var parameterSyntax in syntax.Parameters)
        {
            var parameterName = parameterSyntax.Identifier.Text;
            var parameterType = BindTypeClause(parameterSyntax.Type);

            if (!seenParameterNames.Add(parameterName))
            {
                _diagnostics.ReportParameterAlreadyDeclared(parameterSyntax.Location, parameterName);
            }
            else
            {
                var parameter = new ParameterSymbol(parameterName, parameterType, parameters.Count);
                parameters.Add(parameter);
            }
        }

        var type = BindTypeClause(syntax.Type) ?? TypeSymbol.Void;

        _scope = savedScope;

        var function = new FunctionSymbol(syntax.Identifier.Text, parameters.ToImmutable(), type, syntax,
            typeParamSymbols.ToImmutable());

        if (!_scope.TryDeclareFunction(function))
        {
            _diagnostics.ReportSymbolAlreadyDeclared(syntax.Identifier.Location, function.Name);
        }
    }

    private void BindStructDeclaration(StructDeclarationSyntax syntax)
    {
        var name = syntax.Identifier.Text;

        var typeParameters = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
        foreach (var paramSyntax in syntax.TypeParameters)
        {
            var paramName = paramSyntax.Text;
            var typeParam = new TypeParameterSymbol(paramName, typeParameters.Count);
            typeParameters.Add(typeParam);
        }

        var savedScope = _scope;
        if (typeParameters.Count > 0)
        {
            _scope = new BoundScope(_scope);
            foreach (var typeParam in typeParameters)
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
            }
        }

        _scope = savedScope;

        var structSymbol = new StructSymbol(name, typeParameters.ToImmutable(), fields.ToImmutable());

        if (_structTypes == null)
        {
            _structTypes = ImmutableArray.CreateBuilder<StructSymbol>();
        }

        if (!_scope.TryDeclareType(structSymbol))
        {
            _diagnostics.ReportSymbolAlreadyDeclared(syntax.Identifier.Location, name);
        }
        else
        {
            _structTypes.Add(structSymbol);
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

        var enumSymbol = new EnumSymbol(name, members.ToImmutable());

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

        throw new Exception($"Unexpected type syntax {syntax.Kind}");
    }

    private TypeSymbol? LookupType(string name)
    {
        switch (name)
        {
            case "any":
                return TypeSymbol.Any;
            case "bool":
                return TypeSymbol.Bool;
            case "int":
                return TypeSymbol.Int;
            case "string":
                return TypeSymbol.String;
            case "void":
                return TypeSymbol.Void;
            case "array":
                return TypeSymbol.Array;
            case "map":
                return TypeSymbol.Map;
            case "uint8":
                return TypeSymbol.UInt8;
            case "int8":
                return TypeSymbol.Int8;
            case "uint16":
                return TypeSymbol.UInt16;
            case "int16":
                return TypeSymbol.Int16;
            case "uint64":
                return TypeSymbol.UInt64;
            case "int64":
                return TypeSymbol.Int64;
            case "float":
                return TypeSymbol.Float;
            case "float32":
                return TypeSymbol.Float32;
            case "float64":
                return TypeSymbol.Float64;
            default:
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
            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Location, name);
            return new BoundErrorExpression();
        }

        return new BoundVariableExpression(variable!);
    }

    private BoundExpression BindCallExpression(CallExpressionSyntax syntax, TypeSymbol? expectedType = null)
    {
        // Special case: array_new(size) creates a zero-initialized fixed-length array
        if (syntax.Identifier.Text == "array_new" && syntax.Arguments.Count == 1)
        {
            var sizeArg = BindExpression(syntax.Arguments[0]);
            sizeArg = BindConversion(syntax.Arguments[0].Location, sizeArg, TypeSymbol.Int);
            var elementType = TypeSymbol.Any;
            if (expectedType != null && expectedType.Name == "array" && expectedType.TypeArguments.Length == 1)
                elementType = expectedType.TypeArguments[0];
            // Also check explicit type argument on the call: array_new<int>(8)
            if (elementType == TypeSymbol.Any && syntax.TypeArguments.Length == 1)
                elementType = BindTypeSyntax(syntax.TypeArguments[0]);
            return new BoundArrayNewExpression(elementType, sizeArg);
        }

        if (syntax.Arguments.Count == 1
            && !_scope.TryLookupFunction(syntax.Identifier.Text, out _)
            && LookupType(syntax.Identifier.Text) is TypeSymbol type)
        {
            return BindConversion(syntax.Arguments[0], type, true);
        }

        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var argument in syntax.Arguments)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }

        if (!_scope.TryLookupFunction(syntax.Identifier.Text, out var function))
        {
            var dotNetFunc = TryResolveDotNetFunction(syntax.Identifier.Text);
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
                typeArgs = InferTypeArguments(function, boundArguments.ToImmutable());
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

        if (syntax.Arguments.Count != function.Parameters.Length)
        {
            _diagnostics.ReportWrongArgumentCount(syntax.Location, function.Name, function.Parameters.Length,
                syntax.Arguments.Count);
            return new BoundErrorExpression();
        }

        for (int i = 0; i < syntax.Arguments.Count; i++)
        {
            var argumentLocation = syntax.Arguments[i].Location;
            var argument = boundArguments[i];
            var parameter = function.Parameters[i];
            boundArguments[i] = BindConversion(argumentLocation, argument, parameter.Type);
        }

        return new BoundCallExpression(function, boundArguments.ToImmutable());
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
    private DotNetFunctionSymbol? TryResolveDotNetFunction(string name)
    {
        var registry = DotNetAssemblyRegistry.Instance;

        // Search through all loaded assemblies for a static method matching the name
        foreach (var assembly in registry.GetLoadedAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!type.IsPublic) continue;

                    var methods = registry.GetStaticMethods(type);
                    var matchingMethod = methods.FirstOrDefault(m =>
                        m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    if (matchingMethod != null)
                    {
                        return DotNetFunctionSymbol.FromStaticMethod(matchingMethod);
                    }
                }
            }
            catch
            {
                // Ignore assembly access errors
            }
        }

        return null;
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
                return existing!;
            }
            _diagnostics.ReportVariableAlreadyDeclared(identifier.Location, name);
        }

        return variable;
    }
}