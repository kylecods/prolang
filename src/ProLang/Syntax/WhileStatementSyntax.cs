namespace ProLang.Syntax;

internal sealed class WhileStatementSyntax : StatementSyntax
{
    public WhileStatementSyntax(SyntaxToken whileKeyword, SyntaxToken openToken, ExpressionSyntax condition, SyntaxToken closeToken,StatementSyntax body)
    {
        WhileKeyword = whileKeyword;
        OpenToken = openToken;
        Condition = condition;
        CloseToken = closeToken;
        Body = body;
    }

    public override SyntaxKind Kind => SyntaxKind.WhileStatement;
    
    public SyntaxToken WhileKeyword { get; }
    
    public SyntaxToken OpenToken { get; }

    public ExpressionSyntax Condition { get; }
    
    public SyntaxToken CloseToken { get; }

    public StatementSyntax Body { get; }
}