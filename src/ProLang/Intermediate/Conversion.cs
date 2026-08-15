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

    private static bool IsInteger(TypeSymbol t) =>
        t == TypeSymbol.Int   || t == TypeSymbol.Int8  || t == TypeSymbol.Int16 || t == TypeSymbol.Int64  ||
        t == TypeSymbol.UInt8 || t == TypeSymbol.UInt16|| t == TypeSymbol.UInt32|| t == TypeSymbol.UInt64;

    private static bool IsFloat(TypeSymbol t) =>
        t == TypeSymbol.Float || t == TypeSymbol.Float32 || t == TypeSymbol.Float64;

    private static bool IsNumeric(TypeSymbol t) => IsInteger(t) || IsFloat(t);
}
