using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundFieldAssignmentExpression : BoundExpression
{
    public BoundFieldAssignmentExpression(BoundExpression expression, string fieldName, StructField field, BoundExpression value)
    {
        Expression = expression;
        FieldName = fieldName;
        Field = field;
        Value = value;
        Type = value.Type;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundFieldAssignmentExpression;
    public override TypeSymbol Type { get; }
    
    public BoundExpression Expression { get; }
    public string FieldName { get; }
    public StructField Field { get; }
    public BoundExpression Value { get; }
}