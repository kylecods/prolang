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
        string? msilPath = null;
        var referencePaths = new List<string>();
        var sourcePaths = new List<string>();
        var helpRequested = false;
        var disassemble = false;

        var options = new OptionSet
        {
            "usage: prolang <source-paths> [options]",
            {"r=","The {path} of an assembly to reference", v=> referencePaths.Add(v) },
            {"o=","The output {path} of the assembly to create", v=>outputPath = v },
            {"m=", "The {name} of the module", v => moduleName = v },
            {"d|disassemble", "Compile sources and print IR disassembly to stdout", v => disassemble = true },
            {"msil=", "Disassemble a compiled .dll and print MSIL listing to stdout", v => msilPath = v },
            {"h|help", "Prints help", v=>helpRequested = true},
            {"<>", v=>sourcePaths.Add(v) }
        };

        options.Parse(args);

        if (helpRequested)
        {
            options.WriteOptionDescriptions(Console.Out);
            return 0;
        }

        // MSIL mode: read a compiled .dll and dump its MSIL listing
        if (msilPath != null)
        {
            if (!File.Exists(msilPath))
            {
                Console.Error.WriteLine($"error: file '{msilPath}' doesn't exist");
                return 1;
            }
            MsilDisassembler.Disassemble(msilPath, Console.Out);
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

        var compilation = ProLangCompilation.Create(syntaxTrees.ToArray());

        // IR disassembly mode: bind and print the lowered IR, then also emit the .dll
        if (disassemble)
        {
            try
            {
                var program = compilation.GetBoundProgram();
                if (program.Diagnostics.Any())
                {
                    Console.Error.WriteDiagnostics(program.Diagnostics);
                    return 1;
                }
                Disassembler.Disassemble(program, Console.Out);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

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
