﻿namespace ProLang.Syntax;

internal sealed class NameExpressionSyntax : ExpressionSyntax
{
    public NameExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken identifierToken) : base(syntaxTree)
    {
        IdentifierToken = identifierToken;
    }

    public override SyntaxKind Kind => SyntaxKind.NameExpression;
    
    public SyntaxToken IdentifierToken { get; }
}