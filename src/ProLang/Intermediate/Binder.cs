using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Intermediate;

internal sealed class Binder
{
    private BoundScope _scope;
    
    private readonly Dictionary<VariableSymbol, object> _variables;
    
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

        BoundScope parent = null!;

        while (stack.Count > 0)
        {
            previous = stack.Pop();
            var scope = new BoundScope(parent);

            foreach (var v in previous.Variables)
            {
                scope.TryDeclare(v);

                parent = scope;
            }
        }

        return parent;
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

    private BoundStatement BindForStatementSyntax(ForStatementSyntax syntax)
    {
        var lowerBound = BindExpression(syntax.LowerBound, typeof(bool));
        var upperBound = BindExpression(syntax.UpBound, typeof(bool));

        _scope = new BoundScope(_scope);

        var name = syntax.Identifier.Text;
        var variable = new VariableSymbol(name, true, typeof(int));

        if (!_scope.TryDeclare(variable))
        {
            _diagnostics.ReportVariableAlreadyDeclared(syntax.Identifier.Span,name);
        }

        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundForStatement(variable, lowerBound, upperBound, body);
    }

    private BoundStatement BindWhileStatementSyntax(WhileStatementSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition,typeof(bool));
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundWhileStatement(condition, body);
    }

    private BoundStatement BindIfStatementSyntax(IfStatementSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition,typeof(bool));
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Statement);
        var elseIfStatement = syntax.ElseIf == null ? null : BindElIfStatement(syntax.ElseIf);
        var elseStatement = syntax.Else == null ? null : BindStatement(syntax.Else.Body);

        return new BoundIfStatement(condition, body,elseIfStatement, elseStatement);
    }

    private BoundStatement? BindElIfStatement(ElseIfClauseSyntax syntax)
    {
        var condition = BindExpression(syntax.Condition, typeof(bool));
        var body = BindProLangBlockStatement((BlockStatementSyntax)syntax.Body);

        return new BoundElIfStatement(condition, body);
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, Type targetType)
    {
        var result = BindExpression(syntax);
        if (result.Type != targetType)
            _diagnostics.ReportCannotConvert(syntax.Span, result.Type, targetType);

        return result;
    }

    private BoundStatement BindExpressionSyntax(ExpressionStatementSyntax syntax)
    {
        var expression = BindExpression(syntax.Expression);

        return new BoundExpressionStatement(expression);
    }

    private BoundStatement BindVariableSyntax(VariableStatementSyntax syntax)
    {
        var name = syntax.Identifier.Text;

        var initializer = BindExpression(syntax.Expression);

        var variable = new VariableSymbol(name, false, initializer.Type);

        if (!_scope.TryDeclare(variable))
        {
            _diagnostics.ReportVariableAlreadyDeclared(syntax.Identifier.Span,name);
        }

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

    public BoundExpression BindExpression(ExpressionSyntax syntax)
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
            default:
                throw new Exception($"Unknown syntax kind {syntax.Kind}");
        }
    }

    private BoundExpression BindAssignmentExpression(AssignmentExpressionSyntax syntax)
    {
        var name = syntax.IdentifierToken.Text;
        var boundExpression = BindExpression(syntax.Expression);

        if (!_scope.TryLookup(name, out var variable))
        {
            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Span, name);
            return boundExpression;
        }
        
        if (boundExpression.Type != variable.Type)
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
        var boundOperator = BoundBinaryOperator.Bind(syntax.OperatorToken.Kind, boundLeft.Type,boundRight.Type);
        
        if (boundOperator == null)
        {
            _diagnostics.ReportUndefinedBinaryOperator(syntax.OperatorToken.Span,syntax.OperatorToken.Text,boundLeft.Type, boundRight.Type);
            return boundLeft;
        }
        
        return new BoundBinaryExpression(boundLeft, boundOperator, boundRight);
    }

    private BoundExpression BindUnaryExpression(UnaryExpressionSyntax syntax)
    {
        var boundOperand = BindExpression(syntax.Operand);
        var boundOperator = BoundUnaryOperator.Bind(syntax.Operand.Kind,boundOperand.Type);
        
        if (boundOperator == null)
        {
           _diagnostics.ReportUndefinedUnaryOperator(syntax.OperatorToken.Span,syntax.OperatorToken.Text,boundOperand.Type);
            return boundOperand;
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
        
        if (string.IsNullOrEmpty(name))
        {
            return new BoundLiteralExpression(0);
        }

        if (!_scope.TryLookup(name, out var variable))
        {
            _diagnostics.ReportUndefinedName(syntax.IdentifierToken.Span, name);
            return new BoundLiteralExpression(0);
        }

        return new BoundVariableExpression(variable);
    }


}