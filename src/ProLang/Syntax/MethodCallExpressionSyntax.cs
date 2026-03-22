namespace ProLang.Syntax;

internal sealed class MethodCallExpressionSyntax : ExpressionSyntax
{
    public MethodCallExpressionSyntax(SyntaxTree syntaxTree, ExpressionSyntax expression, SyntaxToken dotToken, SyntaxToken methodName, SyntaxToken openParenthesisToken, SeparatedSyntaxList<ExpressionSyntax> arguments, SyntaxToken closeParenthesisToken)
        : base(syntaxTree)
    {
        Expression = expression;
        DotToken = dotToken;
        MethodName = methodName;
        OpenParenthesisToken = openParenthesisToken;
        Arguments = arguments;
        CloseParenthesisToken = closeParenthesisToken;
    }

    public override SyntaxKind Kind => SyntaxKind.MethodCallExpression;

    public ExpressionSyntax Expression { get; }
    public SyntaxToken DotToken { get; }
    public SyntaxToken MethodName { get; }
    public SyntaxToken OpenParenthesisToken { get; }
    public SeparatedSyntaxList<ExpressionSyntax> Arguments { get; }
    public SyntaxToken CloseParenthesisToken { get; }
}
