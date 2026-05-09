namespace ProLang.Syntax;

internal sealed class CastExpressionSyntax : ExpressionSyntax
{
    public CastExpressionSyntax(SyntaxTree syntaxTree, ExpressionSyntax expression,
        SyntaxToken asKeyword, TypeClauseSyntax type) : base(syntaxTree)
    {
        Expression = expression;
        AsKeyword = asKeyword;
        Type = type;
    }

    public override SyntaxKind Kind => SyntaxKind.CastExpression;

    public ExpressionSyntax Expression { get; }

    public SyntaxToken AsKeyword { get; }

    public TypeClauseSyntax Type { get; }
}
