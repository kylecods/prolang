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
    /// Returns the character at <paramref name="index"/> as a one-character string.
    /// </summary>
    /// <remarks>
    /// ProLang has no character type, so indexing a string yields a string. The IL version of
    /// this boxed the char and went through <c>Convert.ToString(object)</c> to avoid needing the
    /// char's address for an instance call.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the string.</exception>
    public static string CharAt(string str, int index) => str[index].ToString();

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
}
