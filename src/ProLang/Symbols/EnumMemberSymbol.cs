namespace ProLang.Symbols;

/// <summary>
/// One member of an enum, as something that can be pointed at.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EnumMember"/> stays what it is: a record the emitters compare by value, and changing
/// that would ripple through code generation for no gain. This wraps one so that
/// <c>Colour.Green</c> can be hovered, navigated to and renamed like every other name.
/// </para>
/// <para>
/// Instances are created and cached by the binding recorder, one per member, so that two mentions
/// of the same member are the same symbol and therefore group together as references.
/// </para>
/// </remarks>
public sealed class EnumMemberSymbol : Symbol
{
    public EnumMemberSymbol(EnumSymbol declaring, EnumMember member) : base(member.Name)
    {
        Declaring = declaring;
        Member = member;
    }

    public override SymbolKind Kind => SymbolKind.EnumMember;

    /// <summary>The enum this member belongs to.</summary>
    public EnumSymbol Declaring { get; }

    public EnumMember Member { get; }

    public int Value => Member.Value;
}
