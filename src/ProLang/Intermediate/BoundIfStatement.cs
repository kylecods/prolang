namespace ProLang.Intermediate;

internal sealed class BoundIfStatement : BoundStatement
{
    public BoundIfStatement(BoundExpression condition, BoundStatement body, BoundStatement? elseIfStatement, BoundStatement? elseStatement )
    {
        Condition = condition;
        Body = body;
        ElseIfStatement = elseIfStatement;
        ElseStatement = elseStatement;
    }

    public override BoundNodeKind Kind => BoundNodeKind.IfStatement;
    
    public BoundExpression Condition { get; }
    
    public BoundStatement Body { get; }
    
    public BoundStatement? ElseIfStatement { get; }
    
    public BoundStatement? ElseStatement { get; }
}