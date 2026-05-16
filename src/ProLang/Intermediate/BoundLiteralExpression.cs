using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundLiteralExpression : BoundExpression
{
    public BoundLiteralExpression(object value)
    {
        Value = value;

        Type = value switch
        {
            bool => TypeSymbol.Bool,
            int => TypeSymbol.Int,
            uint => TypeSymbol.UInt32,
            byte => TypeSymbol.UInt8,
            sbyte => TypeSymbol.Int8,
            short => TypeSymbol.Int16,
            ushort => TypeSymbol.UInt16,
            long => TypeSymbol.Int64,
            ulong => TypeSymbol.UInt64,
            string => TypeSymbol.String,
            _ => throw new Exception($"Unexpected literal '{value}' of type {value.GetType()}"),
        };
    }
    
    public override BoundNodeKind Kind => BoundNodeKind.BoundLiteralExpression;
    
    public override TypeSymbol Type { get; }
    
    public object Value { get; }

}