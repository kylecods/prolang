using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class MapExpressionSyntax : ExpressionSyntax
{
    public MapExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken leftCurly, SeparatedSyntaxList<MapEntrySyntax> entries, SyntaxToken rightCurly)
        : base(syntaxTree)
    {
        LeftCurly = leftCurly;
        Entries = entries;
        RightCurly = rightCurly;
    }

    public override SyntaxKind Kind => SyntaxKind.MapExpression;

    public SyntaxToken LeftCurly { get; }
    public SeparatedSyntaxList<MapEntrySyntax> Entries { get; }
    public SyntaxToken RightCurly { get; }
}

internal sealed class MapEntrySyntax : SyntaxNode
{
    public MapEntrySyntax(SyntaxTree syntaxTree, ExpressionSyntax key, SyntaxToken colon, ExpressionSyntax value)
        : base(syntaxTree)
    {
        Key = key;
        Colon = colon;
        Value = value;
    }

    public override SyntaxKind Kind => SyntaxKind.MapEntry; // Or create a MapEntry kind if needed

    public ExpressionSyntax Key { get; }
    public SyntaxToken Colon { get; }
    public ExpressionSyntax Value { get; }
}
