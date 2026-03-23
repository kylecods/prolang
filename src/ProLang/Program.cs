using Mono.Options;
using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang;

internal sealed class Program
{
    private static int Main(string[] args)
    {
        string? outputPath = null;
        string? moduleName = null;
        var referencePaths = new List<string>();
        var sourcePaths = new List<string>();
        var helpRequested = false;
        var runMode = false;

        var options = new OptionSet
        {
            "usage: prolang <source-paths> [options]",
            {"r=","The {path} of an assembly to reference", v=> referencePaths.Add(v) },
            {"o=","The output {path} of the assembly to create", v=>outputPath = v },
            {"m=", "The {name} of the module", v => moduleName = v },
            {"run", "Run using the interpreter instead of compiling", v => runMode = true },
            {"h|help", "Prints help", v=>helpRequested = true},
            {"<>", v=>sourcePaths.Add(v) }
        };

        options.Parse(args);

        if (helpRequested)
        {
            options.WriteOptionDescriptions(Console.Out);
            return 0;
        }

        if (sourcePaths.Count == 0) 
        {
            Console.Error.WriteLine("error: need at least one source file");

            return 1;
        }

        if (outputPath == null)
        {
            outputPath = Path.ChangeExtension(sourcePaths[0], ".dll");
        }

        if (moduleName == null) 
        { 
            moduleName = Path.GetFileNameWithoutExtension(outputPath);
        }
        var syntaxTrees = new List<SyntaxTree>();

        var hasErrors = false;

        foreach (var path in sourcePaths)
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

        foreach (var path in referencePaths)
        {
            if (!File.Exists(path)) 
            {
                Console.Error.WriteLine($"{path} does not exist.");

                hasErrors = true; 
                
                continue;
            }
        }

        if (hasErrors)
        {
            return 1;
        }

        if (runMode)
        {
            try
            {
                ProLangCompilation? previous = null;
                foreach (var syntaxTree in syntaxTrees)
                {
                    var scriptCompilation = ProLangCompilation.CreateScript(previous, syntaxTree);
                    var result = scriptCompilation.Evaluate(new Dictionary<VariableSymbol, object>());

                    if (result.Diagnostics.Any())
                    {
                        Console.Error.WriteDiagnostics(result.Diagnostics);
                        return 1;
                    }

                    previous = scriptCompilation;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        //parse the source tree
        var compilation = ProLangCompilation.Create(syntaxTrees.ToArray());
        //run or interpret the content
        try
        {
            var diagnostics = compilation.Emit(moduleName, referencePaths.ToArray(), outputPath);

            if (diagnostics.Any())
            {
                Console.Error.WriteDiagnostics(diagnostics);
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }
}
