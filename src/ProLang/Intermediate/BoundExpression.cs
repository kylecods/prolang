﻿namespace ProLang.Intermediate;

internal abstract class BoundExpression : BoundNode
{
    public abstract Type Type { get; }
}