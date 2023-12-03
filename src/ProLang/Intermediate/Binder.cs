using System.ComponentModel;
using ProLang.Syntax;

namespace ProLang.Intermediate;

internal sealed class Binder
{
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