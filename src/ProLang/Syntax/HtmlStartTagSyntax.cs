namespace ProLang.Syntax;

internal sealed class HtmlStartTagSyntax : StatementSyntax
{
    public SyntaxToken LeftAngleToken { get; }
    
    public SyntaxToken IdentifierToken { get; }

    public SyntaxToken RightAngleToken { get; }

    public HtmlStartTagSyntax(SyntaxToken leftAngleToken, SyntaxToken identifierToken, SyntaxToken rightAngleToken)
    {
        LeftAngleToken = leftAngleToken;
        
        IdentifierToken = identifierToken;
        
        RightAngleToken = rightAngleToken;
    }

    public override SyntaxKind Kind => SyntaxKind.HtmlStartTag;
}