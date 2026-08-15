using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundEnumMemberExpression : BoundExpression
{
    public BoundEnumMemberExpression(EnumSymbol enumType, EnumMember member)
    {
        EnumType = enumType;
        Member = member;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundEnumMemberExpression;
    public override TypeSymbol Type => TypeSymbol.Int;

    public EnumSymbol EnumType { get; }
    public EnumMember Member { get; }
}
