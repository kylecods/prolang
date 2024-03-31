using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundConversionExpression : BoundExpression
{
    public BoundConversionExpression(TypeSymbol type, BoundExpression expression)
    {
        Type = type;
        
        Expression = expression;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundConversionExpression;
    public override TypeSymbol Type { get; }
    
    public BoundExpression Expression { get; }
}