namespace ProLang.Runtime;

/// <summary>
/// Console operations for ProLang's <c>console</c> module.
/// </summary>
/// <remarks>
/// <para>
/// Every method that touches the terminal's presentation — cursor position, colour, visibility —
/// is a no-op when output is redirected. Writing ANSI-style control operations into a pipe or a
/// file corrupts it, and <see cref="Console.SetCursorPosition"/> throws outright on a redirected
/// stream, which would crash any program using the console module non-interactively.
/// </para>
/// <para>
/// These were previously six near-identical blocks of hand-emitted IL, each hand-rolling the
/// redirection check with its own branch targets.
/// </para>
/// </remarks>
public static class ConsoleOps
{
    /// <summary>
    /// Writes text directly to stdout, without a trailing newline.
    /// </summary>
    /// <remarks>
    /// Flushes <see cref="Output"/> first. <c>print()</c> is buffered and this is not, so without
    /// the flush a program that mixed the two would emit all of its direct writes first and its
    /// buffered ones at exit — the opposite of the order it wrote them in.
    /// </remarks>
    public static void Write(string text)
    {
        Output.Flush();

        if (Console.IsOutputRedirected)
        {
            // Still write the text — it is content, not presentation. Only the flush ordering
            // and the cursor/colour operations below need to change under redirection.
            Console.Write(text);
            return;
        }

        Console.Write(text);
    }

    /// <summary>Moves the cursor. No-op when output is redirected.</summary>
    public static void SetCursor(int left, int top)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        // The terminal can be resized between the caller reading its size and this call, so an
        // out-of-range position is a race rather than a programming error.
        try
        {
            Console.SetCursorPosition(left, top);
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    /// <summary>Hides the cursor. No-op when output is redirected.</summary>
    public static void HideCursor()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.CursorVisible = false;
    }

    /// <summary>Sets the foreground colour from a <see cref="ConsoleColor"/> value. No-op when redirected.</summary>
    public static void SetColor(int color)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        if (Enum.IsDefined(typeof(ConsoleColor), color))
        {
            Console.ForegroundColor = (ConsoleColor)color;
        }
    }

    /// <summary>Restores the default colours. No-op when output is redirected.</summary>
    public static void ResetColor()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.ResetColor();
    }

    /// <summary>
    /// Whether a keypress is waiting.
    /// </summary>
    /// <returns>
    /// Always false when stdin is redirected, so an input-polling game loop does not spin or
    /// throw when run non-interactively.
    /// </returns>
    public static bool KeyAvailable() => !Console.IsInputRedirected && Console.KeyAvailable;

    /// <summary>
    /// Reads one keypress without echoing it, as the integer value of its <see cref="ConsoleKey"/>.
    /// </summary>
    /// <returns>-1 when stdin is redirected and no key can be read.</returns>
    public static int ReadKey()
    {
        if (Console.IsInputRedirected)
        {
            return -1;
        }

        return (int)Console.ReadKey(intercept: true).Key;
    }

    /// <summary>Reads a line from stdin, or the empty string at end of input.</summary>
    public static string ReadInput() => Console.ReadLine() ?? string.Empty;
}
