namespace ProLang.Intermediate;

internal class BoundLabelStatement : BoundStatement
{
    public BoundLabelStatement(BoundLabel boundLabel)
    {
        BoundLabel = boundLabel;
    }

    public override BoundNodeKind Kind => BoundNodeKind.LabelStatement;
    
    public BoundLabel BoundLabel { get; }
}