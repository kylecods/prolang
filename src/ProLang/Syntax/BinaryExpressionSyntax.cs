﻿namespace ProLang.Syntax;

internal sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    public BinaryExpressionSyntax(SyntaxTree syntaxTree,ExpressionSyntax left, SyntaxToken operatorToken,
        ExpressionSyntax right) : base(syntaxTree)
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
    }
    
    public override SyntaxKind Kind => SyntaxKind.BinaryExpression;
    
    public ExpressionSyntax Left { get; }
    
    public SyntaxToken OperatorToken { get; }
    
    public ExpressionSyntax Right { get; }
    
}