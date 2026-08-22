namespace ProLang.Syntax;

/// <summary>
/// An argument written as <c>name: value</c> at a call site.
/// </summary>
/// <remarks>
/// <para>
/// Modelled as an expression wrapper rather than as a parallel array of names on
/// <see cref="CallExpressionSyntax"/> so that the argument keeps a span covering the name as well
/// as the value. A diagnostic about an unknown argument name can then point at the name itself
/// instead of at the whole call.
/// </para>
/// <para>
/// The binder unwraps this immediately — see <c>Binder.BindCallExpression</c>. It never reaches the
/// bound tree, so no backend has to know about it.
/// </para>
/// </remarks>
internal sealed class NamedArgumentSyntax : ExpressionSyntax
{
    public NamedArgumentSyntax(SyntaxTree syntaxTree, SyntaxToken identifier, SyntaxToken colonToken,
        ExpressionSyntax expression)
        : base(syntaxTree)
    {
        Identifier = identifier;
        ColonToken = colonToken;
        Expression = expression;
    }

    public override SyntaxKind Kind => SyntaxKind.NamedArgument;

    public SyntaxToken Identifier { get; }

    public SyntaxToken ColonToken { get; }

    public ExpressionSyntax Expression { get; }
}
