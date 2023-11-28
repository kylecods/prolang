using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class HtmlStatementSyntax : StatementSyntax
{
    public HtmlStatementSyntax(SyntaxToken openLeftAngleBracketToken, SyntaxToken openHtmlKeyword, SyntaxToken openRightAngleBracketToken, ImmutableArray<StatementSyntax> statements, SyntaxToken closeLeftAngleBracketToken, SyntaxToken slashToken, SyntaxToken closeHtmlKeyword, SyntaxToken closeRightAngleBracketToken)
    {
        OpenLeftAngleBracketToken = openLeftAngleBracketToken;
        OpenHtmlKeyword = openHtmlKeyword;
        OpenRightAngleBracketToken = openRightAngleBracketToken;
        Statements = statements;
        CloseLeftAngleBracketToken = closeLeftAngleBracketToken;
        SlashToken = slashToken;
        CloseHtmlKeyword = closeHtmlKeyword;
        CloseRightAngleBracketToken = closeRightAngleBracketToken;
    }

    public override SyntaxKind Kind => SyntaxKind.HtmlStatement;
    
    public SyntaxToken OpenLeftAngleBracketToken { get; }
    
    public SyntaxToken OpenHtmlKeyword { get; }
    
    public SyntaxToken OpenRightAngleBracketToken { get; }
    
    public ImmutableArray<StatementSyntax> Statements { get; }
    
    public SyntaxToken CloseLeftAngleBracketToken { get; }
    
    public SyntaxToken SlashToken { get; }
    
    public SyntaxToken CloseHtmlKeyword { get; }
    
    public SyntaxToken CloseRightAngleBracketToken { get; }
    
    
}