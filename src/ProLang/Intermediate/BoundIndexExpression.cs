using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundIndexExpression : BoundExpression
{
    public BoundIndexExpression(BoundExpression expression, BoundExpression index, TypeSymbol type)
    {
        Expression = expression;
        Index = index;
        Type = type;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundIndexExpression;
    public override TypeSymbol Type { get; }
    public BoundExpression Expression { get; }
    public BoundExpression Index { get; }
}
