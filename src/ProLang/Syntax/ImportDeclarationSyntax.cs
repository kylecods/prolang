namespace ProLang.Syntax;

public sealed class ImportDeclarationSyntax : DeclarationSyntax
{
    public ImportDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken importKeyword, SyntaxToken pathToken)
        : base(syntaxTree)
    {
        ImportKeyword = importKeyword;
        PathToken = pathToken;
    }

    public override SyntaxKind Kind => SyntaxKind.ImportDeclaration;

    public SyntaxToken ImportKeyword { get; }

    public SyntaxToken PathToken { get; }

    /// <summary>
    /// The imported path, or <see langword="null"/> when the string was missing from the source.
    /// </summary>
    /// <remarks>
    /// Nullable on purpose. <c>import</c> with nothing after it parses to a missing token whose
    /// value is null, and every caller has to decide what to do about that rather than discover it
    /// as a <see cref="NullReferenceException"/>.
    /// </remarks>
    public string? Path => PathToken.Value as string;
}
