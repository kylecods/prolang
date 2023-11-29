namespace ProLang.Syntax;

internal sealed class HtmlEndTagSyntax : StatementSyntax
{
    public SyntaxToken HtmlCloseToken { get; }
    
    public SyntaxToken IdentifierToken { get; }

    public SyntaxToken RightAngleToken { get; }

    public HtmlEndTagSyntax(SyntaxToken htmlCloseToken, SyntaxToken identifierToken, SyntaxToken rightAngleToken)
    {
        HtmlCloseToken = htmlCloseToken;
        
        IdentifierToken = identifierToken;
        
        RightAngleToken = rightAngleToken;
    }

    public override SyntaxKind Kind => SyntaxKind.HtmlEndTag;
}