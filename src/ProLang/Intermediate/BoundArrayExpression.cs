using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundArrayExpression : BoundExpression
{
    public BoundArrayExpression(ImmutableArray<BoundExpression> elements)
    {
        Elements = elements;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundArrayExpression;
    public override TypeSymbol Type => TypeSymbol.Array;
    public ImmutableArray<BoundExpression> Elements { get; }
}
