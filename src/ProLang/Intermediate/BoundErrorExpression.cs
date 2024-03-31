using ProLang.Symbols;

namespace ProLang.Intermediate;

internal class BoundErrorExpression : BoundExpression
{
    public override BoundNodeKind Kind => BoundNodeKind.BoundErrorExpression;
    public override TypeSymbol Type => TypeSymbol.Error;
}