namespace ProLang.Portable;

/// <summary>The top level of the portable runtime: run one program from its image, and report failure.</summary>
/// <remarks>
/// <para>The C glue starts everything through <c>Runtime.Run</c> (<c>Runtime_Run</c>); the desktop host calls the
///   same method.</para>
/// </remarks>
internal static class Runtime
{
    /// <summary>
    /// Ends the program with <c>message</c> through <c>Host.Fail</c>, which does not return: it throws on the
    /// desktop and unwinds to the C <c>main</c> on the PSP. The <c>int</c> result exists so that a call can stand
    /// where an expression is needed, such as <c>return Runtime.Fail(...)</c>. The failure messages are matched by
    /// tests, so they stay as they are.
    /// </summary>
    internal static int Fail(string message)
    {
        Host.Fail(message);
        return 0;
    }

    /// <summary>
    /// Resets the static state of every class, in a fixed order, so that a program can run again in the same
    /// process.
    /// </summary>
    internal static void Reset()
    {
        Host.Reset();
        Interpreter.Reset();
        Objects.Reset();
        Jit.Reset();
        Natives.Reset();
        Metadata.Reset();
        Loader.Reset();
        TypeSystem.Reset();
        Heap.Reset();
    }

    /// <summary>
    /// Runs a program: resets all state, loads and verifies the image, passes an empty string array if the entry
    /// method takes one argument (any other signature fails with 'entry signature'), compiles every method if
    /// <c>useJit</c> is nonzero, invokes the entry method and flushes the buffered output.
    /// </summary>
    internal static void Run(byte[] image, int length, int useJit)
    {
        Reset();
        Loader.Load(image, length);
        if (Loader.Bodies[Loader.EntryMethod].ArgCount == 1)
        {
            Interpreter.Memory[0] = Objects.NewArray(ElementType.String, 0);
        }
        else if (Loader.Bodies[Loader.EntryMethod].ArgCount != 0)
        {
            Fail("entry signature");
        }

        if (useJit != 0)
        {
            Jit.Compile();
        }

        Interpreter.Invoke(Loader.EntryMethod, 0);
        Natives.Flush();
    }
}
