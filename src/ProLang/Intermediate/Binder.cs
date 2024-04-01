using System.Collections.Immutable;
using ProLang.Lowering;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Intermediate;

internal sealed class Binder
{
    private BoundScope _scope;

    private readonly DiagnosticBag _diagnostics = new ();

    private readonly FunctionSymbol? _function;

    public Binder(BoundScope parent, FunctionSymbol? function)
    {
        _scope = new BoundScope(parent);
        _function = function;

        if (function != null)
        {
            foreach (var parameter in function.Parameters)
            {
                _scope.TryDeclareVariable(parameter);
            }
        }
    }

    public static BoundGlobalScope BindGlobalScope(BoundGlobalScope? previous, GlobalDeclarationSyntax syntax)
    {
        var parentScope = CreateParentScope(previous);
        
        var binder = new Binder(parentScope, null);

        foreach (var function in syntax.Declarations.OfType<FunctionDeclarationSyntax>())
        {
            binder.BindFunctionDeclaration(function);
        }

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        var globalStatements = syntax.Declarations.OfType<GlobalStatementSyntax>();

        foreach (var statementSyntax in globalStatements)
        {
            var statement = binder.BindStatement(statementSyntax.Statement);
            
            statements.Add(statement);
        }

        var functions = binder._scope.GetDeclaredFunctions();
        var variables = binder._scope.GetDeclaredVariables();

        var diagnostics = binder.Diagnostics.ToImmutableArray();

        if (previous != null)
        {
            diagnostics = diagnostics.InsertRange(0, previous.Diagnostics);
        }

        return new BoundGlobalScope(previous,diagnostics,functions,variables,statements.ToImmutableArray());
    }

    public static BoundProgram BindProgram(BoundGlobalScope? globalScope)
    {
        var parentScope = CreateParentScope(globalScope);

        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionSymbol, BoundBlockStatement>();

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var scope = globalScope;

        while (scope != null)
        {
            foreach (var function in scope.Functions)
            {
                var binder = new Binder(parentScope, function);

                var body = binder.BindStatement(function.Declaration!.Body);

                var loweredBody = Lowerer.Lower(body);

                functionBodies.Add(function,loweredBody);
                
                diagnostics.AddRange(binder.Diagnostics);
            }

            scope = scope.Previous;
        }

        var statement = Lowerer.Lower(new BoundBlockStatement(globalScope!.Statements));

        return new BoundProgram(diagnostics.ToImmutable(), functionBodies.ToImmutable(), statement);
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
                _diagnostics.ReportParameterAlreadyDeclared(parameterSyntax.Span,parameterName);
            }
            else
            {
                var parameter = new ParameterSymbol(parameterName, parameterType);
                
                parameters.Add(parameter);
            }
        }

        var type = BindTypeClause(syntax.Type) ?? TypeSymbol.Void;

        if (type != TypeSymbol.Void)
        {
            _diagnostics.XXX_ReportFunctionsAreUnsupported(syntax.Type.Span);
        }

        var function = new FunctionSymbol(syntax.Identifier.Text, parameters.ToImmutable(), type, syntax);

        if (!_scope.TryDeclareFunction(function))
        {
            _diagnostics.ReportSymbolAlreadyDeclared(syntax.Identifier.Span, function.Name);
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

    private BoundStatement BindStatement(StatementSyntax syntax)
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
            default:
                throw new Exception($"Unexpected syntax {syntax.Kind}");
        }
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, bool canBeVoid = false)
    {
        var result = BindInternalExpression(syntax);

        if (!canBeVoid && result.Type == TypeSymbol.Void)
        {
            _diagnostics.ReportExpressionMustHaveValue(syntax.Span);
            return new BoundErrorExpression();
        }

        return result;
    }

    private BoundStatement BindForStatementSyntax(ForStatementSyntax syntax)
    {
        var lowerBound = BindExpression(syntax.LowerBound, TypeSymbol.Bool);
        var upperBound = BindExpression(syntax.UpBound, TypeSymbol.Bool);

        _scope = new BoundScope(_scope);

        var name = syntax.Identifier.Text;
        var variable = new LocalVariableSymbol(name, true, TypeSymbol.Int);

        if (!_scope.TryDeclareVariable(variable))
        {
            _diagnostics.ReportVariableAlreadyDeclared(syntax.Identifier.Span,name);
        }

        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundForStatement(variable, lowerBound, upperBound, body);
    }

    private BoundStatement BindWhileStatementSyntax(WhileStatementSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition,TypeSymbol.Bool);
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundWhileStatement(condition, body);
    }

    private BoundStatement BindIfStatementSyntax(IfStatementSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition,TypeSymbol.Bool);
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Statement);
        var elseIfStatement = syntax.ElseIf == null ? null : BindElIfStatement(syntax.ElseIf);
        var elseStatement = syntax.Else == null ? null : BindStatement(syntax.Else.Body);

        return new BoundIfStatement(condition, body,elseIfStatement, elseStatement);
    }

    private BoundStatement? BindElIfStatement(ElseIfClauseSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition, TypeSymbol.Bool);
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundElIfStatement(condition, body);
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, TypeSymbol targetType)
    {
        return BindConversion(syntax,targetType);
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

        var convertedInitializer = BindConversion(syntax.Expression.Span, initializer, variableType);

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
            _diagnostics.ReportUndefinedType(syntax.Identifier.Span,syntax.Identifier.Text);
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
            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Span, name);
            return boundExpression;
        }

        var convertedExpression = BindConversion(syntax.Expression.Span, boundExpression, variable.Type);

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
        
        var boundOperator = BoundBinaryOperator.Bind(syntax.OperatorToken.Kind, boundLeft.Type,boundRight.Type);
        
        if (boundOperator == null)
        {
            _diagnostics.ReportUndefinedBinaryOperator(syntax.OperatorToken.Span,syntax.OperatorToken.Text,boundLeft.Type, boundRight.Type);
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
        
        var boundOperator = BoundUnaryOperator.Bind(syntax.Operand.Kind,boundOperand.Type);
        
        if (boundOperator == null)
        {
           _diagnostics.ReportUndefinedUnaryOperator(syntax.OperatorToken.Span,syntax.OperatorToken.Text,boundOperand.Type);
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
            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Span, name);
            return new BoundErrorExpression();
        }

        return new BoundVariableExpression(variable!);
    }

    private BoundExpression BindCallExpression(CallExpressionSyntax syntax)
    {
        if (syntax.Arguments.Count == 1 && LookupType(syntax.Identifier.Text) is TypeSymbol type)
        {
            return BindConversion(syntax.Arguments[0], type,true);
        }

        var boundArguments = ImmutableArray.CreateBuilder<BoundExpression>();

        foreach (var argument in syntax.Arguments)
        {
            var boundArgument = BindExpression(argument);
            boundArguments.Add(boundArgument);
        }

        if (!_scope.TryLookupFunction(syntax.Identifier.Text, out var function))
        {
            _diagnostics.ReportUndefinedFunction(syntax.Identifier.Span, syntax.Identifier.Text);
            return new BoundErrorExpression();
        }

        if (syntax.Arguments.Count != function.Parameters.Length)
        {
            _diagnostics.ReportWrongArgumentCount(syntax.Span, function.Name, function.Parameters.Length,
                syntax.Arguments.Count);
            return new BoundErrorExpression();
        }

        for (int i = 0; i < syntax.Arguments.Count; i++)
        {
            var argument = boundArguments[i];
            var parameter = function.Parameters[i];

            if (argument.Type != parameter.Type)
            {
                _diagnostics.ReportWrongArgumentType(syntax.Arguments[i].Span, parameter.Name, parameter.Type, argument.Type);
                return new BoundErrorExpression();
            }
        }

        return new BoundCallExpression(function, boundArguments.ToImmutable());
    }

    private BoundExpression BindConversion(ExpressionSyntax syntax,TypeSymbol type, bool allowExplicit = false)
    {
        var expression = BindExpression(syntax);

        return BindConversion(syntax.Span, expression, type, allowExplicit);
    }

    private BoundExpression BindConversion(TextSpan diagnosticSpan, BoundExpression expression, TypeSymbol type, bool allowExplicit = false)
    {
        var conversion = Conversion.Classify(expression.Type, type);

        if (!conversion.Exists)
        {
            if (expression.Type != TypeSymbol.Error && type != TypeSymbol.Error)
            {
                _diagnostics.ReportCannotConvert(diagnosticSpan, expression.Type, type);
            }

            return new BoundErrorExpression();
        }

        if (!allowExplicit && conversion.IsExplicit)
        {
            _diagnostics.ReportCannotConvertImplicitly(diagnosticSpan,expression.Type,type);
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
            new LocalVariableSymbol(name,isReadonly,type);

        if (declare && !_scope.TryDeclareVariable(variable))
        {
            _diagnostics.ReportVariableAlreadyDeclared(identifier.Span,name);
        }

        return variable;
    }

    private TypeSymbol? LookupType(string name)
    {
        switch (name)
        {
            case "bool":
                return TypeSymbol.Bool;
            case "int":
                return TypeSymbol.Int;
            case "string":
                return TypeSymbol.String;
            default:
                return null;
        }
    }

}