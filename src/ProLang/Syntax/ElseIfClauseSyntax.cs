namespace ProLang.Syntax;

internal class ElseIfClauseSyntax : SyntaxNode
{
    public ElseIfClauseSyntax(SyntaxToken elIfKeyword, ExpressionSyntax condition, StatementSyntax body)
    {
        ElIfKeyword = elIfKeyword;
        
        Condition = condition;
        
        Body = body;
    }

    public override SyntaxKind Kind => SyntaxKind.ElseIfClause;
    
    public SyntaxToken ElIfKeyword { get; }
    
    public ExpressionSyntax Condition { get; }
    
    public StatementSyntax Body { get; }
}