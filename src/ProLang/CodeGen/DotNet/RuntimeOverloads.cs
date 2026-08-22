using ProLang.Symbols;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// Maps a ProLang type onto the runtime overload that accepts it without boxing.
/// </summary>
/// <remarks>
/// <para>
/// <c>print</c> declares its parameter as <c>any</c>, and <c>string(x)</c> used to go through a
/// virtual <c>Object::ToString()</c>. Both boxed every value type, which accounted for most of the
/// allocation in compiled programs. <c>ProLang.Runtime</c> carries typed overloads instead, and
/// this is the single table deciding which one applies.
/// </para>
/// <para>
/// Shared by the MSIL emitter and the C# rendering backend so the two cannot disagree about which
/// call a given expression compiles to.
/// </para>
/// </remarks>
internal static class RuntimeOverloads
{
    /// <summary>
    /// The metadata name of the overload parameter that takes <paramref name="type"/> unboxed.
    /// </summary>
    /// <returns>
    /// A metadata full name, or <see langword="null"/> when no typed overload applies and the
    /// value has to go through <see cref="object"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The IL evaluation stack has nothing narrower than <c>int32</c>, so <c>int8</c>,
    /// <c>uint8</c>, <c>int16</c>, and <c>uint16</c> are already sitting there in that form and
    /// can use the <c>int</c> overload directly — signed types sign-extended, unsigned
    /// zero-extended.
    /// </para>
    /// <para>
    /// <c>uint32</c> cannot share it: same bit pattern, but <c>int32</c> would render anything
    /// above 2^31 as negative. <c>float32</c> keeps its own too, because
    /// <c>Single.ToString()</c> and <c>Double.ToString()</c> disagree on the same value.
    /// </para>
    /// </remarks>
    public static string? ParameterTypeFor(TypeSymbol type)
    {
        // Enums are erased to int32 by the .NET backend.
        if (type is EnumSymbol)
        {
            return "System.Int32";
        }

        if (type == TypeSymbol.Int || type == TypeSymbol.Int8 || type == TypeSymbol.Int16
            || type == TypeSymbol.UInt8 || type == TypeSymbol.UInt16)
        {
            return "System.Int32";
        }

        if (type == TypeSymbol.UInt32) return "System.UInt32";
        if (type == TypeSymbol.Int64) return "System.Int64";
        if (type == TypeSymbol.UInt64) return "System.UInt64";
        if (type == TypeSymbol.Bool) return "System.Boolean";
        if (type == TypeSymbol.Float32) return "System.Single";
        if (type == TypeSymbol.Float64 || type == TypeSymbol.Float) return "System.Double";
        if (type == TypeSymbol.String) return "System.String";

        return null;
    }

    /// <summary>Whether <paramref name="type"/> reaches a runtime overload without boxing.</summary>
    public static bool HasTypedOverload(TypeSymbol type) => ParameterTypeFor(type) != null;

    /// <summary>
    /// Looks through conversions the binder inserted purely to satisfy an <c>any</c> parameter,
    /// recovering the expression's real static type.
    /// </summary>
    /// <remarks>
    /// Only conversions targeting <c>any</c> are unwrapped. A numeric widening, or a cast the
    /// program wrote, changes the value and is left alone.
    /// <para>
    /// The C transpiler has always done this — see <c>CEmitter.UnwrapAny</c>, whose comment
    /// describes the same insight. The .NET backend did not, which is why it boxed.
    /// </para>
    /// </remarks>
    public static Intermediate.BoundExpression UnwrapConversionToAny(Intermediate.BoundExpression expression)
    {
        while (expression is Intermediate.BoundConversionExpression conversion
               && conversion.Type == TypeSymbol.Any)
        {
            expression = conversion.Expression;
        }

        return expression;
    }
}
