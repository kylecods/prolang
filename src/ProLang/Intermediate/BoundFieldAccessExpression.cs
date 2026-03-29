using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundFieldAccessExpression : BoundExpression
{
    public BoundFieldAccessExpression(BoundExpression expression, string fieldName, StructField field)
    {
        Expression = expression;
        FieldName = fieldName;
        Field = field;
        Type = field.Type;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundFieldAccessExpression;
    public override TypeSymbol Type { get; }
    
    public BoundExpression Expression { get; }
    public string FieldName { get; }
    public StructField Field { get; }
}