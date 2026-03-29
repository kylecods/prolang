using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundStructCreationExpression : BoundExpression
{
    public BoundStructCreationExpression(StructSymbol structType, ImmutableArray<BoundExpression> fieldValues)
    {
        StructType = structType;
        FieldValues = fieldValues;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundStructCreationExpression;
    public override TypeSymbol Type => StructType;
    
    public StructSymbol StructType { get; }
    public ImmutableArray<BoundExpression> FieldValues { get; }
}