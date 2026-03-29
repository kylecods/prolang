namespace ProLang.Syntax;

internal sealed class FieldAccessExpressionSyntax : ExpressionSyntax
{
    public FieldAccessExpressionSyntax(SyntaxTree syntaxTree, ExpressionSyntax expression, SyntaxToken dotToken, SyntaxToken fieldName) : base(syntaxTree)
    {
        Expression = expression;
        DotToken = dotToken;
        FieldName = fieldName;
    }

    public ExpressionSyntax Expression { get; }
    public SyntaxToken DotToken { get; }
    public SyntaxToken FieldName { get; }

    public override SyntaxKind Kind => SyntaxKind.FieldAccessExpression;
}