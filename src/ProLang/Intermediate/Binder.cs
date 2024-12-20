﻿using System.Collections.Immutable;
using ProLang.Lowering;
using ProLang.Parse;
using ProLang.Symbols;
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

    public static BoundGlobalScope BindGlobalScope(bool isScript, BoundGlobalScope? previous, ImmutableArray<SyntaxTree> syntaxTrees)
    {
        var parentScope = CreateParentScope(previous);

        var binder = new Binder(isScript, parentScope, null);

        var functionDeclarations =
            syntaxTrees.SelectMany(st => st.Root.Declarations).OfType<FunctionDeclarationSyntax>();

        foreach (var function in functionDeclarations)
        {
            binder.BindFunctionDeclaration(function);
        }

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        var globalStatements =
                syntaxTrees.SelectMany(st => st.Root.Declarations).OfType<GlobalStatementSyntax>();

        foreach (var statementSyntax in globalStatements)
        {
            var statement = binder.BindGlobalStatement(statementSyntax.Statement);

            statements.Add(statement);
        }

        //check any global Statements

        var firstGlobalStatementPerSyntaxTree = syntaxTrees
                                                .Select(st => st.Root.Declarations.OfType<GlobalDeclarationSyntax>().FirstOrDefault())
                                                .Where(g => g != null)
                                                .ToArray();

        if (firstGlobalStatementPerSyntaxTree.Length > 1)
        {
            foreach (var globalStatement in firstGlobalStatementPerSyntaxTree)
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

                    foreach (var globalStatement in firstGlobalStatementPerSyntaxTree)
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

        if (previous != null)
        {
            diagnostics = diagnostics.InsertRange(0, previous.Diagnostics);
        }

        return new BoundGlobalScope(previous, diagnostics, mainFunction, scriptFunction, functions, variables, statements.ToImmutableArray());
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



        return new BoundProgram(previous,diagnostics.ToImmutable(), globalScope.MainFunction, globalScope.ScriptFunction, functionBodies.ToImmutable());
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

    private static BoundScope CreateParentScope(BoundGlobalScope? previous)
    {
        var stack = new Stack<BoundGlobalScope>();

        while (previous != null)
        {
            stack.Push(previous);
            previous = previous.Previous;
        }

        var parent = CreateRootScope();

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
            parent = scope;
        }

        return parent;
    }

    private static BoundScope CreateRootScope()
    {
        var result = new BoundScope(null!);

        foreach (var f in BuiltInFunctions.GetAll())
        {
            result.TryDeclareFunction(f);
        }

        return result;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

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
                                            es.Expression.Kind == BoundNodeKind.BoundAssignmentExpression ||
                                            es.Expression.Kind == BoundNodeKind.BoundCallExpression;

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
        var result = BindInternalExpression(syntax);

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
        var initializer = BindExpression(syntax.Expression);

        var type = BindTypeClause(syntax.TypeClause);

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

        var type = LookupType(syntax.Identifier.Text);

        if (type == null)
        {
            _diagnostics.ReportUndefinedType(syntax.Identifier.Location, syntax.Identifier.Text);
        }

        return type;
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

    public BoundExpression BindInternalExpression(ExpressionSyntax syntax)
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
            default:
                throw new Exception($"Unknown syntax kind {syntax.Kind}");
        }
    }

    private BoundExpression BindAssignmentExpression(AssignmentExpressionSyntax syntax)
    {
        var name = syntax.IdentifierToken.Text;
        var boundExpression = BindExpression(syntax.Expression);

        if (!_scope.TryLookupVariable(name, out var variable))
        {
            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Location, name);
            return boundExpression;
        }

        var convertedExpression = BindConversion(syntax.Expression.Location, boundExpression, variable.Type);

        return new BoundAssignmentExpression(variable, convertedExpression);
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

        var boundOperator = BoundUnaryOperator.Bind(syntax.Operand.Kind, boundOperand.Type);

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
            _diagnostics.ReportUndefinedFunction(syntax.Identifier.Location, syntax.Identifier.Text);
            return new BoundErrorExpression();
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

    private BoundExpression BindConversion(ExpressionSyntax syntax, TypeSymbol type, bool allowExplicit = false)
    {
        var expression = BindExpression(syntax);

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
            _diagnostics.ReportVariableAlreadyDeclared(identifier.Location, name);
        }

        return variable;
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
            default:
                return null;
        }
    }

}