namespace ProLang.Syntax;

internal sealed class ForStatementSyntax : StatementSyntax
{
    public ForStatementSyntax(SyntaxToken forKeyword, SyntaxToken openToken, SyntaxToken identifier, SyntaxToken equalsToken, ExpressionSyntax lowerBound, SyntaxToken toKeyword, ExpressionSyntax upBound,  SyntaxToken closeToken, StatementSyntax body)
    {
        ForKeyword = forKeyword;
        OpenToken = openToken;
        Identifier = identifier;
        EqualsToken = equalsToken;
        LowerBound = lowerBound;
        ToKeyword = toKeyword;
        UpBound = upBound;
        CloseToken = closeToken;
        Body = body;
    }

    public override SyntaxKind Kind => SyntaxKind.ForStatement;
    
    public SyntaxToken ForKeyword { get; }
    
    public SyntaxToken OpenToken { get; }
    
    public SyntaxToken Identifier { get; }
    
    public SyntaxToken EqualsToken { get; }
    
    public ExpressionSyntax LowerBound { get; }
    
    public SyntaxToken ToKeyword { get; }
    
    public ExpressionSyntax UpBound { get; }
    
    public SyntaxToken CloseToken { get; }
    
    public StatementSyntax Body { get; }
}