using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class CallExpressionSyntax : ExpressionSyntax
{
    public CallExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken identifier,
        ImmutableArray<TypeSyntax> typeArguments,
        SyntaxToken openParenthesisToken, SeparatedSyntaxList<ExpressionSyntax> arguments,
        SyntaxToken closeParenthesisToken)
    : base(syntaxTree)
    {
        Identifier = identifier;
        TypeArguments = typeArguments;
        OpenParenthesisToken = openParenthesisToken;
        Arguments = arguments;
        CloseParenthesisToken = closeParenthesisToken;
    }

    public override SyntaxKind Kind => SyntaxKind.CallExpression;
    public SyntaxToken Identifier { get; }
    public ImmutableArray<TypeSyntax> TypeArguments { get; }
    public SyntaxToken OpenParenthesisToken { get; }
    public SeparatedSyntaxList<ExpressionSyntax> Arguments { get; }
    public SyntaxToken CloseParenthesisToken { get; }
}
