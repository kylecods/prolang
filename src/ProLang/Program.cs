using ProLang.Cli;
using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang;

internal sealed class Program
{
    private static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            var repl = new ProLangRepl();
            
            repl.Run();
        }
        else
        {
            
            var paths = GetFilePaths(args);
            var syntaxTrees = new List<SyntaxTree>();

            var hasErrors = false;

            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"error: file '{path}' doesn't exist");

                    hasErrors = true;
                    continue;
                }

                var syntaxTree = SyntaxTree.Load(path);
                syntaxTrees.Add(syntaxTree);
            }

            if (hasErrors)
            {
                return;
            }

            //parse the source tree
            var compilation = new ProLangCompilation(syntaxTrees.ToArray());
            //run or interpret the content
            var result = compilation.Evaluate(new Dictionary<VariableSymbol, object>());
            if (!result.Diagnostics.Any())
            {
                if (result.Value != null)
                {
                    Console.WriteLine(result.Value);
                }
            }
            else
            {
                Console.Error.WriteDiagnostics(result.Diagnostics);
            }
        }

    }

    private static IEnumerable<string?> GetFilePaths(string[] paths)
    {
        var result = new SortedSet<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                result.UnionWith(Directory.EnumerateFiles(path,"*.prl",SearchOption.AllDirectories));
            }
            else
            {
                result.Add(path);
            }
        }

        return result;
    }
}