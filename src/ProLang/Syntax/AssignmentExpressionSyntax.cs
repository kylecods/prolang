﻿namespace ProLang.Syntax;

internal sealed class AssignmentExpressionSyntax : ExpressionSyntax
{
    public AssignmentExpressionSyntax(SyntaxToken identifierToken, SyntaxToken equalsToken, ExpressionSyntax expression)
    {
        IdentifierToken = identifierToken;
        
        EqualsToken = equalsToken;
        
        Expression = expression;
    }

    public SyntaxToken IdentifierToken { get; }
    
    public SyntaxToken EqualsToken { get; }

    public ExpressionSyntax Expression { get; }
    public override SyntaxKind Kind => SyntaxKind.AssignmentExpression;
}