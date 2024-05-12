namespace ProLang.Syntax;

internal sealed class NumberExpressionSyntax : ExpressionSyntax
{
    public NumberExpressionSyntax(SyntaxTree syntaxTree,SyntaxToken numberToken) : base(syntaxTree)
    {
        NumberToken = numberToken;
    }
    
    public override SyntaxKind Kind => SyntaxKind.NumberExpression;
    
    public SyntaxToken NumberToken { get; }
    
}