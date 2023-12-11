using System.Collections.Immutable;
using System.ComponentModel;
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
            case SyntaxKind.ProLangBlockStatement:
            {
                return BindProLangBlockStatement((ProLangBlockStatementSyntax)syntax);
            }
            case SyntaxKind.VariableDeclaration:
            {
                return BindVariableSyntax((VariableStatementSyntax)syntax);
            }
            case SyntaxKind.ExpressionStatement:
            {
                return BindExpressionSyntax((ExpressionStatementSyntax)syntax);
            }
            default:
                throw new Exception($"Unexpected syntax {syntax.Kind}");
        }
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

    private BoundStatement BindProLangBlockStatement(ProLangBlockStatementSyntax syntax)
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

    private BoundExpression BindBinaryExpression(BinaryExpressionSyntax syntax)
    {
        var boundLeft = BindExpression(syntax.Left);
        var boundRight = BindExpression(syntax.Right);
        var boundOperatorKind = BindBinaryOperatorKind(syntax.OperatorToken.Kind, boundLeft.Type,boundRight.Type);
        return new BoundBinaryExpression(boundLeft, boundOperatorKind, boundRight);
    }

    private BoundBinaryOperatorKind? BindBinaryOperatorKind(SyntaxKind operatorTokenKind, Type boundLeftType, Type boundRightType)
    {
        if (boundLeftType != typeof(int) || boundRightType != typeof(int))
        {
            return null;
        }

        switch (operatorTokenKind)
        {
            case SyntaxKind.PlusToken:
                return BoundBinaryOperatorKind.Addition;
            case SyntaxKind.MinusToken:
                return BoundBinaryOperatorKind.Subtraction;
            case SyntaxKind.StarToken:
                return BoundBinaryOperatorKind.Multiplication;
            case SyntaxKind.SlashToken:
                return BoundBinaryOperatorKind.Division;
            default:
                throw new Exception($"Unexpected binary operator {operatorTokenKind}");
        }
    }

    private BoundExpression BindUnaryExpression(UnaryExpressionSyntax syntax)
    {
        var boundOperand = BindExpression(syntax.Operand);
        var boundOperatorKind = BindUnaryOperatorKind(syntax.OperatorToken.Kind, boundOperand.Type);

        return new BoundUnaryExpression(boundOperatorKind, boundOperand);
    }

    private BoundUnaryOperatorKind? BindUnaryOperatorKind(SyntaxKind operatorTokenKind, Type type)
    {
        if (type != typeof(int))
        {
            return null;
        }

        switch (operatorTokenKind)
        {
            case SyntaxKind.PlusToken:
                return BoundUnaryOperatorKind.Identity;
            case SyntaxKind.MinusToken:
                return BoundUnaryOperatorKind.Negation;
            default:
                throw new Exception($"Unknown unary operator {operatorTokenKind}");
        }
    }

    private BoundExpression BindLiteralExpression(LiteralExpressionSyntax syntax)
    {
        var value = syntax.LiteralToken.Value ?? 0;
        return new BoundLiteralExpression(value);
    }
}