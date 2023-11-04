namespace ProLang.Syntax;

internal sealed class SyntaxToken : SyntaxNode
{
    public SyntaxToken(SyntaxKind kind, int position, string text, object value)
    {
        Kind = kind;
        Position = position;
        Text = text;
        Value = value;
    }
    
    public override SyntaxKind Kind { get; set; }
    
    public int Position { get; set; }
    
    public string Text { get; set; }
    
    public object Value { get; set; }

    public override IEnumerable<SyntaxNode> GetChildren()
    {
        return Enumerable.Empty<SyntaxNode>();
    }
}