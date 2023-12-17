namespace ProLang.Intermediate;

internal sealed class BoundWhileStatement(BoundExpression condition, BoundStatement body) : BoundStatement
{
    public override BoundNodeKind Kind => BoundNodeKind.WhileStatement;

    public BoundExpression Condition => condition;

    public BoundStatement Body => body;
}