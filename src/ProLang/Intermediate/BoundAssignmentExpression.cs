using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundAssignmentExpression : BoundExpression
{
    public BoundAssignmentExpression(VariableSymbol variable, BoundExpression expression)
    {
        Variable = variable;
        Expression = expression;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundAssignmentExpression;
    public override Type Type => Expression.Type;
    
    public VariableSymbol Variable { get; }
    
    public BoundExpression Expression { get; }
}