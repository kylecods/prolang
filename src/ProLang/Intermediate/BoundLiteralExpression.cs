using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundLiteralExpression : BoundExpression
{
    public BoundLiteralExpression(object value)
    {
        Value = value;

        if (value is bool)
        {
            Type = TypeSymbol.Bool;
        }else if (value is int)
        {
            Type = TypeSymbol.Int;
        }else if (value is string)
        {
            Type = TypeSymbol.String;
        }
        else
        {
            throw new Exception($"Unexpected literal '{value}' of type {value.GetType()}");
        }
    }
    
    public override BoundNodeKind Kind => BoundNodeKind.BoundLiteralExpression;
    
    public override TypeSymbol Type { get; }
    
    public object Value { get; }

}