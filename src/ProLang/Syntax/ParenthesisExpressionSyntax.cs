﻿namespace ProLang.Syntax;

internal sealed class ParenthesisExpressionSyntax : ExpressionSyntax
{
    public ParenthesisExpressionSyntax(SyntaxToken openParenthesisToken, ExpressionSyntax expression, SyntaxToken closeParenthesisToken)
    {
        OpenParenthesisToken = openParenthesisToken;

        Expression = expression;
        
        CloseParenthesisToken = closeParenthesisToken;
    }

    public override SyntaxKind Kind => SyntaxKind.ParethensisExpression;
    
    public SyntaxToken OpenParenthesisToken { get; }
    public ExpressionSyntax Expression { get; }
    public SyntaxToken CloseParenthesisToken { get; }
    
}