namespace ProLang.Runtime;

/// <summary>
/// Backs the <c>assert</c> builtin exposed by <c>import "test"</c>.
/// </summary>
public static class TestOps
{
    /// <summary>
    /// Does nothing when <paramref name="condition"/> holds; otherwise ends the program with a
    /// non-zero exit code and <paramref name="message"/> on stderr.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Output.Flush"/> runs first, and that is the whole point of routing this through
    /// the runtime rather than emitting a bare <c>throw</c>. <c>print()</c> accumulates into a
    /// <see cref="System.Text.StringBuilder"/> and is only flushed after <c>main</c> returns, so
    /// an assert that threw directly would discard every line the test printed on its way to the
    /// failure — leaving a stack trace and no indication of which case failed or what came before
    /// it. <see cref="ConsoleOps"/> flushes for the same reason before writing directly.
    /// </para>
    /// <para>
    /// The exception type is deliberately plain. ProLang cannot catch anything, so the type
    /// carries no information a program could act on; what matters is the process exit code and
    /// that the message reaches stderr, which is what the execution tests assert on.
    /// </para>
    /// </remarks>
    /// <param name="condition">The condition that was expected to hold.</param>
    /// <param name="message">Text describing what was expected, written to stderr on failure.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="condition"/> is false.</exception>
    public static void Assert(bool condition, string message)
    {
        if (condition)
        {
            return;
        }

        Output.Flush();
        Console.Out.Flush();

        throw new InvalidOperationException("assertion failed: " + message);
    }
}
