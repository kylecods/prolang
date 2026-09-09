using ProLang.Symbols;

namespace ProLang.Intermediate;

/// <summary>
/// The <c>null</c> literal.
/// </summary>
/// <remarks>
/// A node of its own rather than a <see cref="BoundLiteralExpression"/> carrying a null
/// <c>Value</c>. That class takes a non-nullable <c>object</c> and derives the literal's type from
/// it, throwing on anything it does not recognise, so a null would have to be special-cased in its
/// constructor and then guarded against in every consumer that reads <c>Value</c> — the emitters,
/// the printer and the control-flow graph among them.
/// <para>
/// Its type is <see cref="TypeSymbol.Null"/>, which is deliberately absent from
/// <c>TypeSymbol.Primitives</c>: <c>null</c> is a value that converts to any reference type, never
/// a type anyone writes.
/// </para>
/// </remarks>
internal sealed class BoundNullExpression : BoundExpression
{
    public override BoundNodeKind Kind => BoundNodeKind.BoundNullExpression;

    public override TypeSymbol Type => TypeSymbol.Null;
}
