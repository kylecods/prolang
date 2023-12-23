using ProLang.Symbols;

namespace ProLang.Intermediate;

internal class BoundLabelStatement : BoundStatement
{
    public BoundLabelStatement(LabelSymbol label)
    {
        Label = label;
    }

    public override BoundNodeKind Kind => BoundNodeKind.LabelStatement;
    
    public LabelSymbol Label { get; }
}