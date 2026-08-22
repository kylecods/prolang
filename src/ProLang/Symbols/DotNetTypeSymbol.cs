namespace ProLang.Symbols;

/// <summary>
/// A ProLang type that stands for a specific .NET type.
/// </summary>
/// <remarks>
/// <para>
/// Values obtained from .NET used to be typed <c>any</c>, which erased what they actually were.
/// That made instance calls impossible to bind: by the time the binder saw <c>sb.Append("x")</c>
/// it knew only that <c>sb</c> was an object, so there was no way to look <c>Append</c> up.
/// Carrying the <see cref="System.Type"/> keeps that information available for member resolution.
/// </para>
/// <para>
/// It is still <c>System.Object</c> at the IL level — <see cref="ProLang.CodeGen.DotNet.TypeEmitter"/>
/// maps it to the real .NET type, so no boxing or casting is introduced by using it. The name is
/// the .NET type's simple name, which is what a ProLang program writes.
/// </para>
/// </remarks>
public sealed class DotNetTypeSymbol : TypeSymbol
{
    public DotNetTypeSymbol(Type clrType)
        : base(clrType.Name)
    {
        ClrType = clrType;
    }

    /// <summary>The .NET type this stands for.</summary>
    public Type ClrType { get; }

    /// <summary>
    /// Whether a value of this type can be assigned where <paramref name="other"/> is expected.
    /// </summary>
    /// <remarks>
    /// Two .NET types are compatible when one is assignable to the other, which lets a derived
    /// instance flow into a parameter declared as its base.
    /// </remarks>
    public bool IsCompatibleWith(DotNetTypeSymbol other) => other.ClrType.IsAssignableFrom(ClrType);
}
