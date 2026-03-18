namespace ProLang.Syntax;

public sealed class NameTypeSyntax : TypeSyntax
{
    public NameTypeSyntax(SyntaxTree syntaxTree, SyntaxToken identifier) : base(syntaxTree)
    {
        Identifier = identifier;
    }

    public override SyntaxKind Kind => SyntaxKind.NameType;
    public SyntaxToken Identifier { get; }
}
