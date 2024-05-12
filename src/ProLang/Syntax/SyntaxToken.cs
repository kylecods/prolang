using ProLang.Parse;
using ProLang.Text;

namespace ProLang.Syntax;

public sealed class SyntaxToken : SyntaxNode
{
    public SyntaxToken(SyntaxTree syntaxTree,SyntaxKind kind, int position, string text, object value) : base(syntaxTree)
    {
        Kind = kind;
        Position = position;
        Text = text;
        Value = value;
    }
    
    public override SyntaxKind Kind { get;  }
    
    public int Position { get; set; }
    
    public string Text { get; set; }
    
    public object Value { get; set; }

    public override TextSpan Span => new TextSpan(Position, Text?.Length ?? 0);

    public bool IsMissing => Text == null!;

}