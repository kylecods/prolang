namespace ProLang.Lsp;

/// <summary>
/// <c>prolang lsp</c> — runs the language server over standard input and output.
/// </summary>
/// <remarks>
/// A subcommand of the compiler rather than a program of its own, so that an editor extension has
/// nothing extra to find, install or keep in step: whatever <c>prolang</c> is on the path is the
/// compiler that will build the project and the server that will describe it, and they can never
/// be different versions of the language.
/// </remarks>
internal static class LanguageServerCommand
{
    public static int Run(string[] args)
    {
        string? stdRoot = null;
        string? logPath = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--std-root=", StringComparison.Ordinal))
            {
                stdRoot = arg["--std-root=".Length..];
            }
            else if (arg.StartsWith("--log=", StringComparison.Ordinal))
            {
                logPath = arg["--log=".Length..];
            }
            else if (arg is "--stdio" or "-h" or "--help")
            {
                if (arg != "--stdio")
                {
                    PrintHelp();
                    return 0;
                }
            }
            else
            {
                Console.Error.WriteLine($"prolang lsp: unrecognised option '{arg}'");
                PrintHelp();
                return 1;
            }
        }

        TextWriter? log = null;

        try
        {
            if (logPath != null)
            {
                log = new StreamWriter(logPath, append: true);
            }

            // Standard output is the protocol channel from here on: anything else written to it —
            // a stray Console.WriteLine — would be read as a malformed message and desynchronise
            // the connection.
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();

            return new LanguageServer(input, output, stdRoot, log).Run();
        }
        finally
        {
            log?.Dispose();
        }
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("usage: prolang lsp [--stdio] [--std-root=PATH] [--log=PATH]");
        Console.Out.WriteLine();
        Console.Out.WriteLine("  Runs the ProLang language server, speaking the Language Server");
        Console.Out.WriteLine("  Protocol over standard input and output.");
        Console.Out.WriteLine();
        Console.Out.WriteLine("  --stdio          Use standard input and output. The only transport, and the default.");
        Console.Out.WriteLine("  --std-root=PATH  Resolve library imports from PATH instead of the std directory");
        Console.Out.WriteLine("                   beside the compiler. Use it when editing the library itself.");
        Console.Out.WriteLine("  --log=PATH       Append a diagnostic log to PATH.");
    }
}
