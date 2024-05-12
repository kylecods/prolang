namespace ProLang.Syntax;

internal class ElseClauseSyntax : SyntaxNode
{
    public ElseClauseSyntax(SyntaxTree syntaxTree,SyntaxToken elseKeyword, StatementSyntax body) : base(syntaxTree)
    {
        ElseKeyword = elseKeyword;
        
        Body = body;
    }

    public override SyntaxKind Kind => SyntaxKind.ElseClause;
    
    public SyntaxToken ElseKeyword { get; }
    
    public StatementSyntax Body { get; }
}