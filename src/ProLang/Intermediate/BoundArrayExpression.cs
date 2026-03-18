using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundArrayExpression : BoundExpression
{
    public BoundArrayExpression(ImmutableArray<BoundExpression> elements, TypeSymbol type)
    {
        Elements = elements;
        Type = type;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundArrayExpression;
    public override TypeSymbol Type { get; }
    public ImmutableArray<BoundExpression> Elements { get; }
}
