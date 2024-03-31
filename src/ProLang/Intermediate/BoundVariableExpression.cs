using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundVariableExpression : BoundExpression
{
    public BoundVariableExpression(VariableSymbol variable)
    {
        Variable = variable;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundVariableExpression;
    public override TypeSymbol Type => Variable.Type;
    
    public VariableSymbol Variable { get; }
}