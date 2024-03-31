using System.Collections.Immutable;
using Microsoft.VisualBasic;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Intermediate;

internal sealed class Binder
{
    private BoundScope _scope;

    private readonly DiagnosticBag _diagnostics = new ();

    public Binder(BoundScope parent)
    {
        _scope = new BoundScope(parent);
    }

    public static BoundGlobalScope BindGlobalScope(BoundGlobalScope previous, GlobalDeclarationSyntax syntax)
    {
        var parentScope = CreateParentScope(previous);
        
        var binder = new Binder(parentScope);

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();

        var globalStatements = syntax.Declarations.OfType<GlobalStatementSyntax>();

        foreach (var statementSyntax in globalStatements)
        {
            var statement = binder.BindStatement(statementSyntax.Statement);
            
            statements.Add(statement);
        }

        var variables = binder._scope.GetDeclaredVariables();

        var diagnostics = binder.Diagnostics.ToImmutableArray();

        return new BoundGlobalScope(previous,diagnostics,variables,statements.ToImmutableArray());
    }

    private static BoundScope CreateParentScope(BoundGlobalScope previous)
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

            foreach (var v in previous.Variables)
            {
                scope.TryDeclareVariable(v);

                parent = scope;
            }
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
        var variable = new VariableSymbol(name, true, TypeSymbol.Int);

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
        var result = BindExpression(syntax);
        if (targetType != TypeSymbol.Error && result.Type != TypeSymbol.Error &&
            result.Type != targetType)
            _diagnostics.ReportCannotConvert(syntax.Span, result.Type, targetType);

        return result;
    }

    private BoundStatement BindExpressionSyntax(ExpressionStatementSyntax syntax)
    {
        var expression = BindExpression(syntax.Expression, true);

        return new BoundExpressionStatement(expression);
    }

    private BoundStatement BindVariableSyntax(VariableStatementSyntax syntax)
    {
        var initializer = BindExpression(syntax.Expression);

        var variable = BindVariable(syntax.Identifier, false, initializer.Type);

        return new BoundVariableDeclaration(variable, initializer);
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
        
        if (boundExpression.Type != variable!.Type)
        {
            _diagnostics.ReportCannotConvert(syntax.Expression.Span, boundExpression.Type, variable.Type);
            return boundExpression;
        }

        return new BoundAssignmentExpression(variable, boundExpression);
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
            return BindConversion(type, syntax.Arguments[0]);
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

        if (syntax.Arguments.Count != function.Parameter.Length)
        {
            _diagnostics.ReportWrongArgumentCount(syntax.Span, function.Name, function.Parameter.Length,
                syntax.Arguments.Count);
            return new BoundErrorExpression();
        }

        for (int i = 0; i < syntax.Arguments.Count; i++)
        {
            var argument = boundArguments[i];
            var parameter = function.Parameter[i];

            if (argument.Type != parameter.Type)
            {
                _diagnostics.ReportWrongArgumentType(syntax.Span, parameter.Name, parameter.Type, argument.Type);
                return new BoundErrorExpression();
            }
        }

        return new BoundCallExpression(function, boundArguments.ToImmutable());
    }

    private BoundExpression BindConversion(TypeSymbol type, ExpressionSyntax syntax)
    {
        var expression = BindExpression(syntax);
        var conversion = Conversion.Classify(expression.Type, type);

        if (!conversion.Exists)
        {
            _diagnostics.ReportCannotConvert(syntax.Span,expression.Type,type);
            return new BoundErrorExpression();
        }

        return new BoundConversionExpression(type, expression);
    }

    private VariableSymbol BindVariable(SyntaxToken identifier, bool isReadonly, TypeSymbol type)
    {
        var name = identifier.Text ?? "?";
        var declare = !identifier.IsMissing;
        var variable = new VariableSymbol(name, isReadonly, type);

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