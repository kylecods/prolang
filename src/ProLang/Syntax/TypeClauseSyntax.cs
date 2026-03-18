namespace ProLang.Syntax;

public sealed class TypeClauseSyntax : SyntaxNode
{
    public TypeClauseSyntax(SyntaxTree syntaxTree, SyntaxToken colonToken, TypeSyntax type) : base(syntaxTree)
    {
        ColonToken = colonToken;
        Type = type;
    }

    public override SyntaxKind Kind => SyntaxKind.TypeClause;
    
    public SyntaxToken ColonToken { get; }
    
    public TypeSyntax Type { get; }
}