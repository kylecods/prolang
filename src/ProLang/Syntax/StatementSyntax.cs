﻿namespace ProLang.Syntax;

public abstract class StatementSyntax : SyntaxNode
{
    protected StatementSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}