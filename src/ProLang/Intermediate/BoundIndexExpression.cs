using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundIndexExpression : BoundExpression
{
    public BoundIndexExpression(BoundExpression expression, BoundExpression index)
    {
        Expression = expression;
        Index = index;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundIndexExpression;
    public override TypeSymbol Type => TypeSymbol.Any; // Could be more specific if we had generics
    public BoundExpression Expression { get; }
    public BoundExpression Index { get; }
}
