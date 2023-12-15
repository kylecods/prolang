namespace ProLang.Syntax;

internal sealed class IfStatementSyntax : StatementSyntax
{
    public IfStatementSyntax(SyntaxToken ifKeyword, SyntaxToken openToken, ExpressionSyntax condition, SyntaxToken closeToken, StatementSyntax statement, ElseIfClauseSyntax? elseIf, ElseClauseSyntax? @else)
    {
        IfKeyword = ifKeyword;
        OpenToken = openToken;
        Condition = condition;
        CloseToken = closeToken;
        Statement = statement;
        ElseIf = elseIf;
        Else = @else;
    }

    public override SyntaxKind Kind => SyntaxKind.IfStatement;
    
    public SyntaxToken IfKeyword { get; }
    
    public SyntaxToken OpenToken { get;  }
    
    public ExpressionSyntax Condition { get; }
    
    public SyntaxToken CloseToken { get; }

    public StatementSyntax Statement { get; }
    
    public ElseIfClauseSyntax? ElseIf { get; }
    
    public ElseClauseSyntax? Else { get; }
}