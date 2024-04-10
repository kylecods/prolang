namespace ProLang.Syntax;

internal sealed class BreakStatementSyntax : StatementSyntax
{
    public BreakStatementSyntax(SyntaxToken keyword)
    {
        Keyword = keyword;
    }
    public override SyntaxKind Kind => SyntaxKind.BreakStatement;
    
    public SyntaxToken Keyword { get; }
}