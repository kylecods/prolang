namespace ProLang.Syntax;

internal abstract class SyntaxNode
{
    public abstract SyntaxKind Kind { get; set; }

    public abstract IEnumerable<SyntaxNode> GetChildren();
}