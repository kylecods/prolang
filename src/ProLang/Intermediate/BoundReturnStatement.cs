﻿namespace ProLang.Intermediate;

internal sealed class BoundReturnStatement : BoundStatement
{
    public BoundReturnStatement(BoundExpression? expression)
    {
        Expression = expression;
    }

    public override BoundNodeKind Kind => BoundNodeKind.ReturnStatement;
    
    public BoundExpression? Expression { get; }
}