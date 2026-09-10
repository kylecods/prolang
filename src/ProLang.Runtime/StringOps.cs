namespace ProLang.Runtime;

/// <summary>
/// String operations whose ProLang semantics differ from the closest .NET equivalent.
/// </summary>
/// <remarks>
/// Only the ones that need adapting live here. <c>length()</c> and <c>indexOf()</c> map straight
/// onto <see cref="string"/> members and are still emitted as direct calls, since routing them
/// through a wrapper would add a call frame for nothing.
/// </remarks>
public static class StringOps
{
    /// <summary>
    /// Returns the character at <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// A UTF-16 code unit, not a rune: a character outside the basic multilingual plane is two
    /// units and reports each half separately. That matches <c>length()</c> and <c>charCode()</c>,
    /// which are also unit-based, so the three agree with one another.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the string.</exception>
    public static char CharAt(string str, int index) => str[index];

    /// <summary>
    /// Returns the code unit at <paramref name="index"/> as an integer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic counterpart to <see cref="CharAt"/>, which a char now is directly —
    /// <c>c - '0'</c> works without it. It remains for code written before the char type, and
    /// because an <c>int</c> is what the C backends' byte-based strings actually yield.
    /// </para>
    /// <para>
    /// A UTF-16 code unit, not a rune: a character outside the basic multilingual plane is two
    /// units and reports each half separately. That matches <c>length()</c> and <c>charAt()</c>,
    /// which are also unit-based, so the three agree with one another.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the string.</exception>
    public static int CharCode(string str, int index) => str[index];

    /// <summary>
    /// Returns the substring from <paramref name="start"/> up to but not including
    /// <paramref name="end"/>.
    /// </summary>
    /// <remarks>
    /// ProLang's end index is exclusive; <see cref="string.Substring(int, int)"/> takes a length.
    /// The conversion needed two scratch locals per call site when emitted as IL.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside the string, or end precedes start.</exception>
    public static string Substring(string str, int start, int end) => str.Substring(start, end - start);

    // ── string(x) conversions ────────────────────────────────────────────────────────────
    //
    // ProLang's string(x) used to compile to `box` followed by a virtual Object::ToString()
    // call, which allocated once per conversion — 72% of all boxing in the compiled corpus,
    // and a per-iteration allocation in any loop that formats a number. These overloads let
    // the emitter pick by the operand's static type and call directly, with no boxing.
    //
    // Each one calls the same ToString() the virtual dispatch would have reached, so results
    // are byte-for-byte identical to the previous behaviour, culture included.

    /// <summary>Formats a signed 32-bit integer. Covers <c>int</c>, <c>int8</c>, <c>int16</c>, and enums.</summary>
    public static string From(int value) => value.ToString();

    /// <summary>Formats an unsigned 32-bit integer. Covers <c>uint32</c>, <c>uint8</c>, and <c>uint16</c>.</summary>
    public static string From(uint value) => value.ToString();

    /// <summary>Formats a signed 64-bit integer.</summary>
    public static string From(long value) => value.ToString();

    /// <summary>Formats an unsigned 64-bit integer.</summary>
    public static string From(ulong value) => value.ToString();

    /// <summary>Formats a boolean as <c>True</c> or <c>False</c>.</summary>
    public static string From(bool value) => value.ToString();

    /// <summary>
    /// Returns the character as a one-character string.
    /// </summary>
    /// <remarks>
    /// What <c>string(c)</c> and <c>"text" + c</c> compile to. The obvious
    /// <c>value.ToString()</c> does the same thing; spelled out here so the overload set has
    /// every primitive and the emitter's table has no gaps.
    /// </remarks>
    public static string From(char value) => value.ToString();

    /// <summary>Formats a single-precision float.</summary>
    public static string From(float value) => value.ToString();

    /// <summary>Formats a double-precision float.</summary>
    public static string From(double value) => value.ToString();

    /// <summary>Returns the string unchanged. Present so the emitter has a uniform target.</summary>
    public static string From(string value) => value;

    /// <summary>
    /// Formats a value whose type is only known at runtime.
    /// </summary>
    /// <remarks>
    /// The fallback for a genuine <c>any</c>, and the only overload that can involve boxing —
    /// which by then the caller has already paid for, because the value reached here as an
    /// <see cref="object"/>.
    /// </remarks>
    public static string From(object? value) => value?.ToString() ?? string.Empty;
}
