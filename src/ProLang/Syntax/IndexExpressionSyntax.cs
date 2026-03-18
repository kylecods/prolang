namespace ProLang.Syntax;

internal sealed class IndexExpressionSyntax : ExpressionSyntax
{
    public IndexExpressionSyntax(SyntaxTree syntaxTree, ExpressionSyntax expression, SyntaxToken leftBracket, ExpressionSyntax index, SyntaxToken rightBracket)
        : base(syntaxTree)
    {
        Expression = expression;
        LeftBracket = leftBracket;
        Index = index;
        RightBracket = rightBracket;
    }

    public override SyntaxKind Kind => SyntaxKind.IndexExpression;

    public ExpressionSyntax Expression { get; }
    public SyntaxToken LeftBracket { get; }
    public ExpressionSyntax Index { get; }
    public SyntaxToken RightBracket { get; }
}
