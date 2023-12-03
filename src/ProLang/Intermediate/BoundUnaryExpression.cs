namespace ProLang.Intermediate;

internal sealed class BoundUnaryExpression : BoundExpression
{
    public BoundUnaryExpression(BoundUnaryOperatorKind? operatorKind, BoundExpression operand)
    {
        OperatorKind = operatorKind;
        Operand = operand;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundUnaryExpression;
    public override Type Type => Operand.Type;
    
    public BoundUnaryOperatorKind? OperatorKind { get; }
    public BoundExpression Operand { get; }
    
}