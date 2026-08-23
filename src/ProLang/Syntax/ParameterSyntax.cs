namespace ProLang.Syntax;

public sealed class ParameterSyntax : SyntaxNode
{
    internal ParameterSyntax(SyntaxTree syntaxTree, SyntaxToken identifier, TypeClauseSyntax type,
        SyntaxToken? equalsToken = null, ExpressionSyntax? defaultValue = null) : base(syntaxTree)
    {
        Identifier = identifier;
        Type = type;
        EqualsToken = equalsToken;
        DefaultValue = defaultValue;
    }

    public override SyntaxKind Kind => SyntaxKind.Parameter;

    public SyntaxToken Identifier { get; }

    public TypeClauseSyntax Type { get; }

    /// <summary>The <c>=</c> of a default value, or <see langword="null"/> for a required parameter.</summary>
    public SyntaxToken? EqualsToken { get; }

    /// <summary>
    /// The default value expression, or <see langword="null"/> for a required parameter.
    /// </summary>
    /// <remarks>
    /// Internal because <c>ExpressionSyntax</c> is. That also keeps it out of the reflection-driven
    /// <see cref="SyntaxNode.GetChildren"/>, which only walks public properties — harmless here,
    /// since every diagnostic about a default reports the expression's own location rather than
    /// the parameter's span.
    /// </remarks>
    internal ExpressionSyntax? DefaultValue { get; }
}
