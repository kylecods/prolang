﻿namespace ProLang.Intermediate;

internal sealed class BoundBinaryExpression : BoundExpression
{
    public BoundBinaryExpression(BoundExpression left,BoundBinaryOperator op, BoundExpression right)
    {
        Left = left;
        Op = op;
        Right = right;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundBinaryExpression;
    public override Type Type => Left.Type;
    
    public BoundExpression Left { get; }
    
    public BoundBinaryOperator Op { get; }
    
    public BoundExpression Right { get; }
    
}