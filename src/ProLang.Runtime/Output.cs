using System.Text;

namespace ProLang.Runtime;

/// <summary>
/// Collects everything a ProLang program prints and flushes it when the program ends.
/// </summary>
/// <remarks>
/// <para>
/// ProLang buffers <c>print()</c> rather than writing straight through, so that a program's
/// output appears as one block and can be captured whole for testing. The compiler emits a call
/// to <see cref="Initialize"/> before the user's <c>main</c> and to <see cref="Flush"/> after it.
/// </para>
/// <para>
/// This used to be three methods of hand-written IL synthesised into every compiled assembly
/// (<c>__InitializeOutput</c>, <c>__AppendToOutput</c>, <c>__FlushOutput</c>). Expressing it as
/// ordinary C# means it can be read, changed, and tested directly.
/// </para>
/// </remarks>
public static class Output
{
    private static readonly StringBuilder Buffer = new();

    /// <summary>Starts a fresh output buffer. Called once before the program's entry point.</summary>
    public static void Initialize() => Buffer.Clear();

    /// <summary>
    /// Appends one <c>print()</c> call's value, followed by a newline.
    /// </summary>
    /// <param name="value">
    /// Any ProLang value. <c>any</c> is <see cref="object"/> at the IL level, so this accepts
    /// everything and formats via <see cref="Convert.ToString(object?)"/>.
    /// </param>
    public static void Write(object? value) => Buffer.AppendLine(Convert.ToString(value));

    /// <summary>Writes everything buffered so far to stdout and empties the buffer.</summary>
    /// <remarks>
    /// Emptying matters: <see cref="ConsoleOps"/> flushes through here before writing directly,
    /// so that buffered and direct output interleave in the order the program produced them.
    /// Leaving the buffer intact would reprint it on the next flush.
    /// </remarks>
    public static void Flush()
    {
        if (Buffer.Length == 0)
        {
            return;
        }

        // The buffer already ends with a newline from the last AppendLine, so Write rather than
        // WriteLine keeps a program's output free of a spurious trailing blank line.
        Console.Write(Buffer.ToString());
        Buffer.Clear();
    }
}
