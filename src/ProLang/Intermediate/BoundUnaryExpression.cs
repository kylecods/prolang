namespace ProLang.Intermediate;

internal sealed class BoundUnaryExpression : BoundExpression
{
    public BoundUnaryExpression(BoundUnaryOperator op, BoundExpression operand)
    {
        Op = op;
        Operand = operand;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundUnaryExpression;
    public override Type Type => Op.Type;
    
    public BoundUnaryOperator Op { get; }
    public BoundExpression Operand { get; }
    
}