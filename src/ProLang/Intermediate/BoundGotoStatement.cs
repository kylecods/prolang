﻿using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundGotoStatement : BoundStatement
{
    public BoundGotoStatement(LabelSymbol label)
    {
        Label = label;
    }

    public override BoundNodeKind Kind => BoundNodeKind.GotoStatement;
    
    public LabelSymbol Label { get; }
}