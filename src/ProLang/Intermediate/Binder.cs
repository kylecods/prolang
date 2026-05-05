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

    public Binder(bool isScript, BoundScope parent, FunctionSymbol? function)
    {
        _scope = new BoundScope(parent);
        _isScript = isScript;
        _function = function;

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
        var functionDeclarations = allDeclarations.OfType<FunctionDeclarationSyntax>();
        var globalStatements = allDeclarations.OfType<GlobalStatementSyntax>();

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

        var functions = binder._scope.GetDeclaredFunctions();

        FunctionSymbol mainFunction;

        FunctionSymbol scriptFunction;

        if (isScript)
        {
            mainFunction = null;

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
            mainFunction = functions.FirstOrDefault(f => f.Name == "main");
            scriptFunction = null;

            if (mainFunction != null)
            {
                if (mainFunction.Type != TypeSymbol.Void || mainFunction.Parameters.Any())
                {
                    binder.Diagnostics.ReportMainFunctionMustHaveCorrectSignature(mainFunction.Declaration.Identifier.Location);
                }
            }

            if (globalStatements.Any())
            {
                if (mainFunction != null)
                {
                    binder.Diagnostics.ReportCannotMixMainAndGlobalStatements(mainFunction.Declaration.Identifier.Location);

                    foreach (var globalStatement in firstGlobalStatementList)
                    {
                        binder.Diagnostics.ReportCannotMixMainAndGlobalStatements(globalStatement.Location);

                    }
                }
                else
                {
                    mainFunction = new FunctionSymbol("main", ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void, null);
                }
            }
        }

        var diagnostics = binder.Diagnostics.ToImmutableArray();
        var variables = binder._scope.GetDeclaredVariables();
        var structTypes = binder._structTypes?.ToImmutable() ?? ImmutableArray<StructSymbol>.Empty;

        if (previous != null)
        {
            diagnostics = diagnostics.InsertRange(0, previous.Diagnostics);
        }

        return new BoundGlobalScope(previous, diagnostics, mainFunction, scriptFunction, functions, variables, statements.ToImmutableArray(), structTypes);
    }

    public static BoundProgram BindProgram(bool isScript, BoundProgram previous, BoundGlobalScope? globalScope)
    {
        var parentScope = CreateParentScope(globalScope);

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var function in globalScope.Functions)
        {
            var binder = new Binder(isScript, parentScope, function);

            var body = binder.BindStatement(function.Declaration!.Body);

            var loweredBody = Lowerer.Lower(body);

            if (function.Type != TypeSymbol.Void && !ControlFlowGraph.AllPathsReturn(loweredBody))
            {
                binder._diagnostics.ReportAllPathsMustReturn(function.Declaration.Identifier.Location);
            }

            functionBodies.Add(function, loweredBody);

            diagnostics.AddRange(binder.Diagnostics);
        }

        if (globalScope.MainFunction != null && globalScope.Statements.Any())
        {
            var body = Lowerer.Lower(new BoundBlockStatement(globalScope.Statements));

            functionBodies.Add(globalScope.MainFunction, body);
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

            var body = Lowerer.Lower(new BoundBlockStatement(statements));

            functionBodies.Add(globalScope.ScriptFunction, body);
        }



        return new BoundProgram(previous,diagnostics.ToImmutable(), globalScope.MainFunction, globalScope.ScriptFunction, functionBodies.ToImmutable(), globalScope.StructTypes);
    }

    private void BindFunctionDeclaration(FunctionDeclarationSyntax syntax)
    {
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
                var parameter = new ParameterSymbol(parameterName, parameterType,parameters.Count);

                parameters.Add(parameter);
            }
        }

        var type = BindTypeClause(syntax.Type) ?? TypeSymbol.Void;

        var function = new FunctionSymbol(syntax.Identifier.Text, parameters.ToImmutable(), type, syntax);

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
                if (expression != null)
                {
                    _diagnostics.ReportInvalidReturnExpression(syntax.Expression.Location, _function.Name);
                }
                else
                {
                    if (expression == null)
                    {
                        _diagnostics.ReportMissingReturnExpression(syntax.ReturnKeyword.Location, _function.Type);
                    }
                    else
                    {
                        expression = BindConversion(syntax.Expression.Location, expression, _function.Type);
                    }
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
                return BindCallExpression((CallExpressionSyntax)syntax);
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

        var fieldValues = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var initializer in syntax.Initializers)
        {
            var value = BindExpression(initializer.Expression);
            fieldValues.Add(value);
        }

        return new BoundStructCreationExpression(structType, fieldValues.ToImmutable());
    }

    private BoundExpression BindFieldAccessExpression(FieldAccessExpressionSyntax syntax)
    {
        var expression = BindExpression(syntax.Expression);

        if (expression.Type == TypeSymbol.Error)
        {
            return new BoundErrorExpression();
        }

        var fieldName = syntax.FieldName.Text;

        if (!_scope.TryLookupType(expression.Type.Name, out var structType))
        {
            _diagnostics.ReportInvalidFieldAccess(syntax.Expression.Location, expression.Type);
            return new BoundErrorExpression();
        }

        var field = structType.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null)
        {
            _diagnostics.ReportUndefinedField(syntax.FieldName.Location, structType.Name, fieldName);
            return new BoundErrorExpression();
        }

        return new BoundFieldAccessExpression(expression, fieldName, field);
    }

    private BoundExpression BindParenthesizedExpression(ParenthesisExpressionSyntax syntax)
    {
        return BindExpression(syntax.Expression);
    }

    private BoundExpression BindBinaryExpression(BinaryExpressionSyntax syntax)
    {
        var boundLeft = BindExpression(syntax.Left);
        var boundRight = BindExpression(syntax.Right);

        if (boundLeft.Type == TypeSymbol.Error || boundRight.Type == TypeSymbol.Error)
        {
            return new BoundErrorExpression();
        }

        var boundOperator = BoundBinaryOperator.Bind(syntax.OperatorToken.Kind, boundLeft.Type, boundRight.Type);

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

    private BoundExpression BindCallExpression(CallExpressionSyntax syntax)
    {
        if (syntax.Arguments.Count == 1 && LookupType(syntax.Identifier.Text) is TypeSymbol type)
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
            // Try to resolve as a .NET static method (e.g., "WriteLine" from System.Console)
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
        { "push", BuiltInFunctions.Push },
        { "pop", BuiltInFunctions.Pop },
        { "getAt", BuiltInFunctions.GetAt },
        { "length", BuiltInFunctions.Length },
    };

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
                var dotNetFunc = ResolveDotNetStaticMethod(typeName, methodName);
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

        // Handle array methods
        if (receiver.Type.Name == "array")
        {
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

        // Handle .NET instance method calls on 'any' type
        if (receiver.Type == TypeSymbol.Any)
        {
            var dotNetFunc = ResolveDotNetInstanceMethod(methodName);
            if (dotNetFunc != null)
            {
                return BindDotNetInstanceMethodCall(syntax, receiver, dotNetFunc, syntax.Arguments);
            }
        }

        _diagnostics.ReportUndefinedMethod(syntax.MethodName.Location, methodName, receiver.Type);
        return new BoundErrorExpression();
    }

    /// <summary>
    /// Resolves a .NET static method by type name and method name.
    /// </summary>
    private DotNetFunctionSymbol? ResolveDotNetStaticMethod(string typeName, string methodName)
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

        // First try to find a static method
        var methods = registry.GetStaticMethods(dotNetType);
        var matchingMethod = methods.FirstOrDefault(m =>
            m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

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
    /// Resolves a .NET instance method (for 'any' type objects).
    /// </summary>
    private DotNetFunctionSymbol? ResolveDotNetInstanceMethod(string methodName)
    {
        // We don't know the actual type at bind time for 'any' types,
        // so we create a placeholder that will be resolved at runtime
        return null;
    }

    /// <summary>
    /// Binds a call to a .NET function.
    /// </summary>
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