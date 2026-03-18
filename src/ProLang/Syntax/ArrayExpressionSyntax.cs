namespace ProLang.Syntax;

internal sealed class ArrayExpressionSyntax : ExpressionSyntax
{
    public ArrayExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken leftBracket, SeparatedSyntaxList<ExpressionSyntax> elements, SyntaxToken rightBracket)
        : base(syntaxTree)
    {
        LeftBracket = leftBracket;
        Elements = elements;
        RightBracket = rightBracket;
    }

    public override SyntaxKind Kind => SyntaxKind.ArrayExpression;

    public SyntaxToken LeftBracket { get; }
    public SeparatedSyntaxList<ExpressionSyntax> Elements { get; }
    public SyntaxToken RightBracket { get; }
}
