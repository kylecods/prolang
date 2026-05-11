using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundArrayNewExpression : BoundExpression
{
    public BoundArrayNewExpression(TypeSymbol elementType, BoundExpression sizeExpression)
    {
        ElementType = elementType;
        SizeExpression = sizeExpression;
        Type = TypeSymbol.Array.WithArgs(elementType);
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundArrayNewExpression;
    public override TypeSymbol Type { get; }
    public TypeSymbol ElementType { get; }
    public BoundExpression SizeExpression { get; }
}
