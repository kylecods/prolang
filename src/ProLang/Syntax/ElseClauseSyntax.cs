namespace ProLang.Syntax;

internal class ElseClauseSyntax : SyntaxNode
{
    public ElseClauseSyntax(SyntaxToken elseKeyword, StatementSyntax body)
    {
        ElseKeyword = elseKeyword;
        
        Body = body;
    }

    public override SyntaxKind Kind => SyntaxKind.ElseClause;
    
    public SyntaxToken ElseKeyword { get; }
    
    public StatementSyntax Body { get; }
}