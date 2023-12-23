﻿using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundConditionalGotoStatement : BoundStatement
{
    public BoundConditionalGotoStatement(LabelSymbol label, BoundExpression condition, bool jumpIfFalse)
    {
        Label = label;
        Condition = condition;
        JumpIfFalse = jumpIfFalse;
    }

    public override BoundNodeKind Kind => BoundNodeKind.ConditionalGotoStatement;
    
    public LabelSymbol Label { get; }
    
    public BoundExpression Condition { get; }
    
    public bool JumpIfFalse { get; }
}