using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundMapExpression : BoundExpression
{
    public BoundMapExpression(ImmutableArray<(BoundExpression Key, BoundExpression Value)> entries, TypeSymbol type)
    {
        Entries = entries;
        Type = type;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundMapExpression;
    public override TypeSymbol Type { get; }
    public ImmutableArray<(BoundExpression Key, BoundExpression Value)> Entries { get; }
}
