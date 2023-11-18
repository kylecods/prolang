namespace ProLang.Syntax;

internal sealed class VariableStatementSyntax : StatementSyntax
{
    public VariableStatementSyntax(SyntaxToken keyword, SyntaxToken identifier, SyntaxToken equalsToken, ExpressionSyntax expression)
    {
        Keyword = keyword;
        Identifier = identifier;
        EqualsToken = equalsToken;
        Expression = expression;
    }
    public override SyntaxKind Kind => SyntaxKind.VariableDeclaration;
    public SyntaxToken Keyword { get; }
    public SyntaxToken Identifier { get; }
    public SyntaxToken EqualsToken { get; }
    public ExpressionSyntax Expression { get; }
    public override IEnumerable<SyntaxNode> GetChildren()
    {
        yield return Keyword;
        yield return Identifier;
        yield return EqualsToken;
        yield return Expression;
    }
}