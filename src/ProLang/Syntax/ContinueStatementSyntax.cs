namespace ProLang.Syntax;

internal sealed class ContinueStatementSyntax : StatementSyntax
{
    public ContinueStatementSyntax(SyntaxToken keyword)
    {
        Keyword = keyword;
    }

    public override SyntaxKind Kind => SyntaxKind.ContinueStatement;
    
    public SyntaxToken Keyword { get; }
}