namespace ProLang.Syntax;

public sealed class GenericTypeSyntax : TypeSyntax
{
    public GenericTypeSyntax(SyntaxTree syntaxTree, SyntaxToken identifier, SyntaxToken lessThanToken, SeparatedSyntaxList<TypeSyntax> arguments, SyntaxToken greaterThanToken) : base(syntaxTree)
    {
        Identifier = identifier;
        LessThanToken = lessThanToken;
        Arguments = arguments;
        GreaterThanToken = greaterThanToken;
    }

    public override SyntaxKind Kind => SyntaxKind.GenericType;
    public SyntaxToken Identifier { get; }
    public SyntaxToken LessThanToken { get; }
    public SeparatedSyntaxList<TypeSyntax> Arguments { get; }
    public SyntaxToken GreaterThanToken { get; }
}
