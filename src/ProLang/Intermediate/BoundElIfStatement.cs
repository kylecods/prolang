namespace ProLang.Intermediate;

internal sealed class BoundElIfStatement : BoundStatement
{
    public BoundElIfStatement(BoundExpression condition, BoundStatement body)
    {
        Condition = condition;
        Body = body;
    }

    public override BoundNodeKind Kind => BoundNodeKind.ElIfStatement;
    
    public BoundExpression Condition { get; }
    
    public BoundStatement Body { get; }
}