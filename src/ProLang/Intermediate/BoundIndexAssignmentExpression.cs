using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundIndexAssignmentExpression : BoundExpression
{
    public BoundIndexAssignmentExpression(BoundExpression expression, BoundExpression index, BoundExpression value)
    {
        LHS = expression;
        Index = index;
        RHS = value;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundIndexAssignmentExpression;
    public override TypeSymbol Type => RHS.Type;
    public BoundExpression LHS { get; }
    public BoundExpression Index { get; }
    public BoundExpression RHS { get; }
}
