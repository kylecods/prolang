using ProLang.Symbols;

namespace ProLang.Intermediate;

internal abstract class BoundExpression : BoundNode
{
    public abstract TypeSymbol Type { get; }
}