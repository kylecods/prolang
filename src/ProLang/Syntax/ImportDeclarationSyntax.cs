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

    public string Path => (string)PathToken.Value;
}
