using ProLang.Cli;

namespace ProLang;

internal sealed class Program
{
    private static void Main()
    {
        var repl = new ProLangRepl();
        
        repl.Run();
    }
}