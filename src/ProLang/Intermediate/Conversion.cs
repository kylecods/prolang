using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class Conversion
{
    public static readonly Conversion None     = new(exists: false, isIdentity: false, isExplicit: false);
    public static readonly Conversion Identity = new(exists: true,  isIdentity: true,  isExplicit: false);
    public static readonly Conversion Implicit = new(exists: true,  isIdentity: false, isExplicit: false);
    public static readonly Conversion Explicit = new(exists: true,  isIdentity: false, isExplicit: true);

    private Conversion(bool exists, bool isIdentity, bool isExplicit)
    {
        Exists     = exists;
        IsIdentity = isIdentity;
        IsExplicit = isExplicit;
    }

    public bool Exists     { get; }
    public bool IsIdentity { get; }
    public bool IsExplicit { get; }

    public static Conversion Classify(TypeSymbol from, TypeSymbol to)
    {
        if (from == to)
            return Identity;

        // A .NET value is System.Object at the IL level, so it converts wherever `any` does.
        //
        // Without this, giving .NET values their real type instead of `any` would have been a
        // breaking change: `let g: string = Guid.NewGuid()` compiled before only because the
        // result was typed `any`. Keeping the type has to add resolution, not remove conversions.
        if (from is DotNetTypeSymbol || to is DotNetTypeSymbol)
        {
            return ClassifyDotNet(from, to);
        }

        // Explicit casts to string (emitter handles Box + ToString)
        if (to == TypeSymbol.String &&
            (from == TypeSymbol.Int  || from == TypeSymbol.Bool  ||
             from == TypeSymbol.Any  || from == TypeSymbol.UInt8 ||
             from == TypeSymbol.Int8 || from == TypeSymbol.UInt16||
             from == TypeSymbol.Int16|| from == TypeSymbol.UInt32||
             from == TypeSymbol.Int64|| from == TypeSymbol.UInt64))
            return Explicit;

        // Implicit signed-integer widenings
        if (from == TypeSymbol.Int8  && (to == TypeSymbol.Int16 || to == TypeSymbol.Int || to == TypeSymbol.Int64)) return Implicit;
        if (from == TypeSymbol.Int16 && (to == TypeSymbol.Int   || to == TypeSymbol.Int64))  return Implicit;
        if (from == TypeSymbol.Int   && to == TypeSymbol.Int64)  return Implicit;

        // Implicit unsigned widenings (also safe into larger signed types)
        if (from == TypeSymbol.UInt8  && (to == TypeSymbol.UInt16 || to == TypeSymbol.UInt32 || to == TypeSymbol.UInt64 ||
                                          to == TypeSymbol.Int16   || to == TypeSymbol.Int    || to == TypeSymbol.Int64))  return Implicit;
        if (from == TypeSymbol.UInt16 && (to == TypeSymbol.UInt32 || to == TypeSymbol.UInt64 ||
                                          to == TypeSymbol.Int     || to == TypeSymbol.Int64)) return Implicit;
        if (from == TypeSymbol.UInt32 && (to == TypeSymbol.UInt64 || to == TypeSymbol.Int64)) return Implicit;

        // Implicit float widenings: float32 → float64, float → float64
        if ((from == TypeSymbol.Float32 || from == TypeSymbol.Float) &&
            (to == TypeSymbol.Float64 || to == TypeSymbol.Float))
            return Implicit;

        // Explicit float narrowing: float64 → float32
        if ((from == TypeSymbol.Float64 || from == TypeSymbol.Float) && to == TypeSymbol.Float32)
            return Explicit;

        // Implicit int → float (all integer types fit in float64; float32 may lose precision but allowed implicitly like C#)
        if (IsInteger(from) && IsFloat(to)) return Implicit;

        // Explicit float → int
        if (IsFloat(from) && IsInteger(to)) return Explicit;

        // All integer ↔ integer conversions are implicit (C-like narrowing is allowed)
        if (IsInteger(from) && IsInteger(to)) return Implicit;

        // Explicit narrowing / cross-sign conversions between other numeric types
        if (IsNumeric(from) && IsNumeric(to)) return Explicit;

        return None;
    }

    /// <summary>
    /// Classifies a conversion where either side is a .NET type.
    /// </summary>
    /// <remarks>
    /// These behave as <c>any</c> did, with one addition: between two .NET types the conversion
    /// exists only when one is actually assignable to the other, so a <c>StringBuilder</c> cannot
    /// silently flow into a parameter expecting a <c>Guid</c>.
    /// </remarks>
    private static Conversion ClassifyDotNet(TypeSymbol from, TypeSymbol to)
    {
        if (from is DotNetTypeSymbol fromDotNet && to is DotNetTypeSymbol toDotNet)
        {
            return fromDotNet.IsCompatibleWith(toDotNet) ? Implicit
                : toDotNet.IsCompatibleWith(fromDotNet) ? Explicit
                : None;
        }

        // Widening into `any` loses nothing — both are System.Object.
        if (to == TypeSymbol.Any)
        {
            return Implicit;
        }

        // Out of `any` into a specific .NET type is a downcast, as it is for any other type.
        if (from == TypeSymbol.Any)
        {
            return Explicit;
        }

        // Formatting a .NET value as text, the same explicit conversion `any` gets.
        if (to == TypeSymbol.String)
        {
            return Explicit;
        }

        return None;
    }

    private static bool IsInteger(TypeSymbol t) =>
        t == TypeSymbol.Int   || t == TypeSymbol.Int8  || t == TypeSymbol.Int16 || t == TypeSymbol.Int64  ||
        t == TypeSymbol.UInt8 || t == TypeSymbol.UInt16|| t == TypeSymbol.UInt32|| t == TypeSymbol.UInt64;

    private static bool IsFloat(TypeSymbol t) =>
        t == TypeSymbol.Float || t == TypeSymbol.Float32 || t == TypeSymbol.Float64;

    private static bool IsNumeric(TypeSymbol t) => IsInteger(t) || IsFloat(t);
}
