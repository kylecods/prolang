using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundCastExpression : BoundExpression
{
    public BoundCastExpression(BoundExpression expression, TypeSymbol targetType)
    {
        Expression = expression;
        TargetType = targetType;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundCastExpression;
    public override TypeSymbol Type => TargetType;

    public BoundExpression Expression { get; }
    public TypeSymbol TargetType { get; }
}
