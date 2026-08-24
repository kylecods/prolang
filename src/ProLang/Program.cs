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
        // Verbs are matched before the option set gets a look, because its "<>" handler collects
        // every bare argument as a source path — so `prolang lsp` would otherwise be a request to
        // compile a file called "lsp".
        if (args.Length > 0 && args[0] == "lsp")
        {
            return Lsp.LanguageServerCommand.Run(args[1..]);
        }

        string? outputPath = null;
        string? moduleName = null;
        string? msilPath = null;
        string? cOutputDir = null;
        string? target = null;
        string? framework = null;
        string? iconPath = null;
        var referencePaths = new List<string>();
        var sourcePaths = new List<string>();
        var helpRequested = false;
        var appHost = false;
        var disassemble = false;
        var emitC = false;
        var emitPsp = false;
        var emitCSharp = false;

        var options = new OptionSet
        {
            "usage: prolang <source-paths> [options]",
            {"r=","The {path} of an assembly to reference", v=> referencePaths.Add(v) },
            {"o=","The output {path} of the assembly to create", v=>outputPath = v },
            {"m=", "The {name} of the module", v => moduleName = v },
            {"d|disassemble", "Compile sources and print IR disassembly to stdout", v => disassemble = true },
            {"msil=", "Disassemble a compiled .dll and print MSIL listing to stdout", v => msilPath = v },
            {"emit-c", "Transpile to C99 and write to .prolang/artifacts/", v => emitC = true },
            {"emit-psp", "Transpile to C99 for PSP and write PSP wrapper + Makefile", v => emitPsp = true },
            {"emit-csharp", "Render the lowered program as C# for inspection (does not compile)", v => emitCSharp = true },
            {"c-output=", "Override output directory for C transpilation", v => cOutputDir = v },
            {"target=", "Subsystem of the emitted assembly: {library} (default), console, or winexe", v => target = v },
            {"framework=", "Shared framework to request: {name}, or the shorthand 'windowsdesktop'", v => framework = v },
            {"apphost", "Also write a native launcher next to the assembly, so it runs without 'dotnet'", v => appHost = true },
            {"icon=", "Embed an .ico {path} in the launcher, as the program's icon. Implies --apphost", v => iconPath = v },
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

        // References are not checked here: -r accepts bare names and .csproj paths as well as
        // .dll paths, so whether one resolves is for the resolver to decide. It reports where it
        // looked, which a bare File.Exists check here could not.

        if (hasErrors)
        {
            return 1;
        }

        var compilation = ProLangCompilation.Create(referencePaths.ToArray(), syntaxTrees.ToArray());

        // C transpile mode
        if (emitC)
        {
            if (moduleName == null)
                moduleName = Path.GetFileNameWithoutExtension(sourcePaths[0]);

            // Default output dir: .prolang/artifacts/ next to the first source file
            if (cOutputDir == null)
            {
                var srcDir = Path.GetDirectoryName(Path.GetFullPath(sourcePaths[0])) ?? Directory.GetCurrentDirectory();
                cOutputDir = Path.Combine(srcDir, ".prolang", "artifacts");
            }

            try
            {
                var diagnostics = compilation.EmitC(moduleName, cOutputDir);
                if (diagnostics.Any())
                {
                    Console.Error.WriteDiagnostics(diagnostics);
                    return 1;
                }
                Console.WriteLine($"C output written to: {cOutputDir}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        // PSP transpile mode
        if (emitPsp)
        {
            if (moduleName == null)
                moduleName = Path.GetFileNameWithoutExtension(sourcePaths[0]);

            if (cOutputDir == null)
            {
                var srcDir = Path.GetDirectoryName(Path.GetFullPath(sourcePaths[0])) ?? Directory.GetCurrentDirectory();
                cOutputDir = Path.Combine(srcDir, ".prolang", "psp");
            }

            try
            {
                var diagnostics = compilation.EmitPsp(moduleName, cOutputDir);
                if (diagnostics.Any())
                {
                    Console.Error.WriteDiagnostics(diagnostics);
                    return 1;
                }
                Console.WriteLine($"PSP output written to: {cOutputDir}");
                Console.WriteLine($"  Run: (cd {cOutputDir} && make -f Makefile.psp)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        // C# rendering mode: write the lowered program out as readable C# for inspection.
        if (emitCSharp)
        {
            var outputDir = cOutputDir
                ?? Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(sourcePaths[0])) ?? Directory.GetCurrentDirectory(),
                    ".prolang", "csharp");

            try
            {
                var diagnostics = compilation.EmitCSharp(moduleName, outputDir);
                if (diagnostics.Any())
                {
                    Console.Error.WriteDiagnostics(diagnostics);
                    return 1;
                }
                Console.WriteLine($"C# output written to: {outputDir}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

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

        if (!TryBuildEmitOptions(target, framework, out var emitOptions))
        {
            return 1;
        }

        try
        {
            var diagnostics = compilation.Emit(moduleName, referencePaths.ToArray(), outputPath, emitOptions);

            if (diagnostics.Any())
            {
                Console.Error.WriteDiagnostics(diagnostics);
                return 1;
            }

            // The launcher is written after the assembly it launches, and a failure to write one
            // is reported but not fatal: the .dll is still a complete program, runnable with
            // `dotnet`. Refusing to have compiled at all because a packaging step did not work
            // would be the wrong trade.
            //
            // An icon needs a launcher to live in, so asking for one asks for the other. The
            // alternative — rejecting `--icon` without `--apphost` — would be a rule to remember
            // in exchange for nothing.
            if (appHost || iconPath != null)
            {
                var wantsWindow = emitOptions.TargetKind == EmitTargetKind.WindowsApplication;

                if (AppHost.TryCreate(outputPath, wantsWindow, out var executablePath, out var appHostError))
                {
                    Console.WriteLine($"launcher written to: {executablePath}");

                    if (iconPath != null)
                    {
                        if (IconEmbedder.TryEmbed(executablePath, iconPath, out var iconError))
                        {
                            Console.WriteLine($"icon embedded from: {iconPath}");
                        }
                        else
                        {
                            Console.Error.WriteLine($"warning: no icon embedded — {iconError}");
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"warning: no launcher written — {appHostError}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    /// <summary>
    /// Turns the <c>--target</c> and <c>--framework</c> arguments into <see cref="EmitOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--target=winexe</c> implies the Windows Desktop framework unless <c>--framework</c> says
    /// otherwise. The two are almost always wanted together, and getting only the first produces
    /// an assembly that fails to resolve <c>System.Windows.Forms</c> with no console attached to
    /// report it on — a silent non-start rather than an error message.
    /// </para>
    /// <para>
    /// An unrecognised value is rejected rather than ignored. Silently falling back to the default
    /// would mean <c>--target=winexec</c> produces a console assembly and a mystery console window.
    /// </para>
    /// </remarks>
    private static bool TryBuildEmitOptions(string? target, string? framework, out EmitOptions options)
    {
        options = EmitOptions.Default;

        var targetKind = EmitTargetKind.Library;

        if (target != null)
        {
            switch (target.ToLowerInvariant())
            {
                case "library" or "dll":
                    targetKind = EmitTargetKind.Library;
                    break;
                case "console" or "exe":
                    targetKind = EmitTargetKind.ConsoleApplication;
                    break;
                case "winexe" or "windows":
                    targetKind = EmitTargetKind.WindowsApplication;
                    break;
                default:
                    Console.Error.WriteLine(
                        $"error: unknown target '{target}'. Expected 'library', 'console', or 'winexe'.");
                    return false;
            }
        }

        var frameworkName = framework?.ToLowerInvariant() switch
        {
            null when targetKind == EmitTargetKind.WindowsApplication => EmitOptions.WindowsDesktopFrameworkName,
            null => EmitOptions.DefaultFrameworkName,
            "windowsdesktop" or "desktop" => EmitOptions.WindowsDesktopFrameworkName,
            "netcore" or "default" => EmitOptions.DefaultFrameworkName,
            _ => framework!,
        };

        options = new EmitOptions(targetKind, frameworkName);
        return true;
    }
}
