using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundGotoStatement : BoundStatement
{
    public BoundGotoStatement(BoundLabel boundLabel)
    {
        BoundLabel = boundLabel;
    }

    public override BoundNodeKind Kind => BoundNodeKind.GotoStatement;
    
    public BoundLabel BoundLabel { get; }
}