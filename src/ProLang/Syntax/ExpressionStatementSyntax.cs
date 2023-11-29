namespace ProLang.Syntax;

internal sealed class ExpressionStatementSyntax : StatementSyntax
{
    public ExpressionStatementSyntax(ExpressionSyntax expression)
    {
        Expression = expression;
    }

    public ExpressionSyntax Expression { get; }
    public override SyntaxKind Kind => SyntaxKind.ExpressionStatement;
}