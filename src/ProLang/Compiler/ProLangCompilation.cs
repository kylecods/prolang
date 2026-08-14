using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interop;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Syntax;

namespace ProLang.Compiler;

public sealed class ProLangCompilation
{

    private BoundGlobalScope? _globalScope;
    private readonly ImmutableArray<Diagnostic> _importDiagnostics;
    private readonly ImmutableHashSet<string> _importedModules;

    /// <summary>
    /// Full paths of DLLs that were resolved from the compiler's lib/ directory.
    /// These are copied next to the output assembly at emit time so the compiled
    /// program can find them at runtime without any manual deployment step.
    /// </summary>
    private readonly ImmutableHashSet<string> _libAssemblyPaths;

    internal BoundGlobalScope GlobalScope
    {
        get
        {
            if (_globalScope == null)
            {
                var globalScope = Binder.BindGlobalScope(IsScript,Previous?.GlobalScope, SyntaxTrees, _importedModules);

                Interlocked.CompareExchange(ref _globalScope, globalScope, null);
            }

            return _globalScope;
        }
    }
 

    private ProLangCompilation(bool isScript, ProLangCompilation? previous, ImmutableArray<Diagnostic> importDiagnostics, ImmutableHashSet<string> importedModules, ImmutableHashSet<string> libAssemblyPaths, params SyntaxTree[] syntaxTrees)
    {
        IsScript = isScript;
        Previous = previous;
        SyntaxTrees = syntaxTrees.ToImmutableArray();
        _importDiagnostics = importDiagnostics;
        _importedModules = importedModules;
        _libAssemblyPaths = libAssemblyPaths;
    }

    public static ProLangCompilation Create(params SyntaxTree[] syntaxTrees)
    {
        var (resolved, diagnostics, importedModules, libAssemblyPaths) = ResolveAllImports(syntaxTrees.ToImmutableArray());
        return new ProLangCompilation(isScript:false, previous: null, diagnostics, importedModules, libAssemblyPaths, resolved.ToArray());
    }

    private static (ImmutableArray<SyntaxTree> Trees, ImmutableArray<Diagnostic> Diagnostics, ImmutableHashSet<string> ImportedModules, ImmutableHashSet<string> LibAssemblyPaths) ResolveAllImports(
        ImmutableArray<SyntaxTree> syntaxTrees)
    {
        var allTrees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Seed visited with the initially provided files
        foreach (var st in syntaxTrees)
        {
            var filePath = st.Text.FileName;
            if (!string.IsNullOrEmpty(filePath))
            {
                visited.Add(Path.GetFullPath(filePath));
            }
        }

        var queue = new Queue<SyntaxTree>(syntaxTrees);

        while (queue.Count > 0)
        {
            var tree = queue.Dequeue();
            allTrees.Add(tree);

            var importingFilePath = tree.Text.FileName;
            var importingDir = string.IsNullOrEmpty(importingFilePath)
                ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(Path.GetFullPath(importingFilePath));

            foreach (var decl in tree.Root.Declarations)
            {
                if (decl is not ImportDeclarationSyntax import)
                    continue;

                var importPath = import.Path;

                // Handle .NET namespace imports: "dotnet:System.Text.Json"
                if (importPath.StartsWith("dotnet:", StringComparison.OrdinalIgnoreCase))
                {
                    var namespaceName = importPath["dotnet:".Length..];
                    if (BuiltInModule.RegisterDotNetNamespace(namespaceName))
                    {
                        importedModules.Add(importPath);
                    }
                    else
                    {
                        diagnostics.Add(new Diagnostic(import.PathToken.Location,
                            $"Could not find .NET namespace '{namespaceName}'. Ensure the assembly is loaded."));
                    }
                    continue;
                }

                // Handle .NET assembly file imports: "assembly:/path/to/MyLib.dll"
                if (importPath.StartsWith("assembly:", StringComparison.OrdinalIgnoreCase))
                {
                    var assemblyPath = importPath["assembly:".Length..];

                    // Try relative to importing file first
                    string? resolvedAssemblyPath = null;
                    if (!string.IsNullOrEmpty(importingDir))
                    {
                        var candidate = Path.GetFullPath(Path.Combine(importingDir, assemblyPath));
                        if (File.Exists(candidate))
                        {
                            resolvedAssemblyPath = candidate;
                        }
                    }

                    // Try as absolute path
                    if (resolvedAssemblyPath == null && Path.IsPathRooted(assemblyPath))
                    {
                        if (File.Exists(assemblyPath))
                        {
                            resolvedAssemblyPath = Path.GetFullPath(assemblyPath);
                        }
                    }

                    // Try relative to CWD
                    if (resolvedAssemblyPath == null)
                    {
                        var candidate = Path.GetFullPath(assemblyPath);
                        if (File.Exists(candidate))
                        {
                            resolvedAssemblyPath = candidate;
                        }
                    }

                    // Try the compiler's lib/ directory for bare names (no path separators).
                    // Enables "assembly:WinFormsHelper" without specifying an explicit path.
                    if (resolvedAssemblyPath == null
                        && !assemblyPath.Contains('/')
                        && !assemblyPath.Contains('\\'))
                    {
                        var libDir = Path.Combine(AppContext.BaseDirectory, "lib");
                        var dllName = assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                            ? assemblyPath : assemblyPath + ".dll";
                        var candidate = Path.GetFullPath(Path.Combine(libDir, dllName));
                        if (File.Exists(candidate))
                        {
                            resolvedAssemblyPath = candidate;
                            libAssemblyPaths.Add(candidate); // deploy alongside output at emit time
                        }
                    }

                    if (resolvedAssemblyPath != null)
                    {
                        var assembly = BuiltInModule.LoadAssemblyFromFile(resolvedAssemblyPath);
                        if (assembly != null)
                        {
                            importedModules.Add(importPath);
                            // Register all public namespaces from the assembly
                            RegisterAssemblyNamespaces(assembly);
                        }
                        else
                        {
                            diagnostics.Add(new Diagnostic(import.PathToken.Location,
                                $"Could not load .NET assembly '{resolvedAssemblyPath}'. The file may not be a valid .NET assembly."));
                        }
                    }
                    else
                    {
                        diagnostics.Add(new Diagnostic(import.PathToken.Location,
                            $"Could not find assembly file '{assemblyPath}'."));
                    }
                    continue;
                }

                // Handle built-in modules
                if (BuiltInModule.TryGetModule(importPath, out _))
                {
                    importedModules.Add(importPath);
                    continue;
                }

                // Resolve: try relative to importing file, then relative to CWD
                string? resolvedPath = null;

                if (!string.IsNullOrEmpty(importingDir))
                {
                    var candidate = Path.GetFullPath(Path.Combine(importingDir, importPath));
                    if (File.Exists(candidate))
                    {
                        resolvedPath = candidate;
                    }
                }

                if (resolvedPath == null)
                {
                    var candidate = Path.GetFullPath(importPath);
                    if (File.Exists(candidate))
                    {
                        resolvedPath = candidate;
                    }
                }

                // Try the std/ directory next to the compiler executable
                if (resolvedPath == null)
                {
                    var stdDir = Path.Combine(AppContext.BaseDirectory, "std");
                    var stdName = importPath.EndsWith(".prl", StringComparison.OrdinalIgnoreCase)
                        ? importPath : importPath + ".prl";
                    var candidate = Path.GetFullPath(Path.Combine(stdDir, stdName));
                    if (File.Exists(candidate))
                        resolvedPath = candidate;
                }

                // Try the compiler's lib/ directory as a native (.dll) stdlib assembly.
                // Allows plain `import "WinFormsHelper"` to resolve lib/WinFormsHelper.dll
                // without an explicit "assembly:" prefix or file path.
                if (resolvedPath == null)
                {
                    var libDir = Path.Combine(AppContext.BaseDirectory, "lib");
                    var dllName = importPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        ? importPath : importPath + ".dll";
                    var candidate = Path.GetFullPath(Path.Combine(libDir, dllName));
                    if (File.Exists(candidate))
                    {
                        var asm = BuiltInModule.LoadAssemblyFromFile(candidate);
                        if (asm != null)
                        {
                            importedModules.Add(importPath);
                            RegisterAssemblyNamespaces(asm);
                            libAssemblyPaths.Add(candidate); // deploy alongside output at emit time
                        }
                        else
                        {
                            diagnostics.Add(new Diagnostic(import.PathToken.Location,
                                $"Could not load stdlib assembly '{importPath}' from '{candidate}'."));
                        }
                        continue; // handled — do NOT enqueue as a source tree
                    }
                }

                if (resolvedPath == null)
                {
                    diagnostics.Add(new Diagnostic(import.PathToken.Location,
                        $"Could not find file or module '{importPath}'."));
                    continue;
                }

                if (!visited.Add(resolvedPath))
                {
                    // Already imported — skip (not an error for non-circular cases)
                    continue;
                }

                var importedTree = SyntaxTree.Load(resolvedPath);
                queue.Enqueue(importedTree);
            }
        }

        return (allTrees.ToImmutable(), diagnostics.ToImmutable(), importedModules.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), libAssemblyPaths.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Registers all public namespaces from an assembly as importable modules.
    /// </summary>
    private static void RegisterAssemblyNamespaces(System.Reflection.Assembly assembly)
    {
        try
        {
            var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<Type> exportedTypes;
            try
            {
                exportedTypes = assembly.GetExportedTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                exportedTypes = ex.Types.Where(t => t != null)!;
            }

            foreach (var type in exportedTypes)
            {
                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    namespaces.Add(type.Namespace);
                }
            }

            foreach (var ns in namespaces)
            {
                BuiltInModule.RegisterDotNetNamespace(ns);
            }
        }
        catch
        {
            // Some assemblies may throw on GetExportedTypes
        }
    }

    private BoundProgram GetProgram()
    {
        var previous = Previous == null ? null : Previous.GetProgram();

        return Binder.BindProgram(IsScript, previous, GlobalScope);
    }

    public BoundProgram GetBoundProgram() => GetProgram();

    public void EmitTree(TextWriter writer)
    {
        if (GlobalScope.MainFunction != null)
        {
            EmitTree(GlobalScope.MainFunction,writer);
        }
    }
    public void EmitTree(FunctionSymbol symbol, TextWriter writer)
    {
        var program = GetProgram();

        symbol.WriteTo(writer);
        writer.WriteLine();

        if (!program.Functions.TryGetValue(symbol, out var body))
        {
            return;
        }

        body.WriteTo(writer);
    }

    public ProLangCompilation? Previous { get; }

    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

    public bool IsScript { get; }

    public FunctionSymbol MainFunction => GlobalScope.MainFunction;

    public ImmutableArray<FunctionSymbol> Functions => GlobalScope.Functions;

    public ImmutableArray<VariableSymbol> Variables => GlobalScope.Variables;

    public IEnumerable<Symbol> GetSymbols()
    {
        var submission = this;

        var seenSymbols = new HashSet<string>();

        while (submission != null)
        {
            var builtInFunctions = BuiltInModule.GetAllFunctions().ToList();

            foreach (var function in submission.Functions)
            {
                if (seenSymbols.Add(function.Name))
                {
                    yield return function;
                }
            }

            foreach (var variable in submission.Variables)
            {
                if (seenSymbols.Add(variable.Name))
                {
                    yield return variable;
                }
            }

            foreach (var builtIn in builtInFunctions)
            {
                if (seenSymbols.Add(builtIn.Name))
                {
                    yield return builtIn;
                }
            }

            submission = submission.Previous;

        }
    }

    public ImmutableArray<Diagnostic> EmitC(string moduleName, string outputDir)
    {
        if (_importDiagnostics.Any()) return _importDiagnostics;

        var parseDiagnostics = SyntaxTrees.SelectMany(st => st.Diagnostics);
        var diagnostics = parseDiagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();
        if (diagnostics.Any()) return diagnostics;

        var program = GetProgram();
        if (program.Diagnostics.Any()) return program.Diagnostics;

        Directory.CreateDirectory(outputDir);

        var cFile = Path.Combine(outputDir, $"{moduleName}.c");
        var emitDiagnostics = CEmitter.Emit(program, moduleName, cFile);

        // Copy runtime headers (base.h, prl_*.h, prolang_runtime.h)
        CRuntimeHeader.WriteAll(outputDir);

        // Build scripts — use plain string concatenation to avoid brace-escape issues
        var n = moduleName;
        var buildSh =
            "#!/bin/sh\n" +
            $"# Build {n} - generated by ProLang\n" +
            "set -e\n" +
            "cd \"$(dirname \"$0\")\"\n" +
            "CC=${CC:-cc}\n" +
            $"\"$CC\" -std=c99 -Wall -O2 -o {n} {n}.c -lm\n" +
            $"echo \"Built: ./{n}\"\n";

        var buildBat =
            "@echo off\r\n" +
            $"rem Build {n} - generated by ProLang\r\n" +
            "cd /d \"%~dp0\"\r\n" +
            "setlocal\r\n" +
            "\r\n" +
            "rem Try cl.exe already on PATH (Developer Command Prompt)\r\n" +
            "where cl >nul 2>&1\r\n" +
            "if %errorlevel% equ 0 goto :have_cl\r\n" +
            "\r\n" +
            "rem Auto-discover Visual Studio via vswhere\r\n" +
            "set \"VSWHERE=%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vswhere.exe\"\r\n" +
            "if not exist \"%VSWHERE%\" set \"VSWHERE=%ProgramFiles%\\Microsoft Visual Studio\\Installer\\vswhere.exe\"\r\n" +
            "if not exist \"%VSWHERE%\" goto :try_gcc\r\n" +
            "\r\n" +
            "for /f \"usebackq tokens=*\" %%i in (`\"%VSWHERE%\" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set \"VS_PATH=%%i\"\r\n" +
            "if not defined VS_PATH goto :try_gcc\r\n" +
            "set \"VCVARS=%VS_PATH%\\VC\\Auxiliary\\Build\\vcvarsall.bat\"\r\n" +
            "if not exist \"%VCVARS%\" goto :try_gcc\r\n" +
            "call \"%VCVARS%\" x64 >nul 2>&1\r\n" +
            "\r\n" +
            ":have_cl\r\n" +
            $"cl /TC /std:c11 /O2 {n}.c /Fe:{n}.exe\r\n" +
            "if errorlevel 1 (echo Build failed & endlocal & exit /b 1)\r\n" +
            $"echo Built: {n}.exe\r\n" +
            "endlocal\r\n" +
            "exit /b 0\r\n" +
            "\r\n" +
            ":try_gcc\r\n" +
            "where gcc >nul 2>&1\r\n" +
            "if %errorlevel% equ 0 goto :have_gcc\r\n" +
            "where clang >nul 2>&1\r\n" +
            "if %errorlevel% equ 0 goto :have_clang\r\n" +
            "echo ERROR: No C compiler found. Install Visual Studio, MinGW, or LLVM.\r\n" +
            "endlocal\r\n" +
            "exit /b 1\r\n" +
            "\r\n" +
            ":have_gcc\r\n" +
            $"gcc -std=c99 -O2 -o {n}.exe {n}.c -lm\r\n" +
            "if errorlevel 1 (echo Build failed & endlocal & exit /b 1)\r\n" +
            $"echo Built: {n}.exe\r\n" +
            "endlocal\r\n" +
            "exit /b 0\r\n" +
            "\r\n" +
            ":have_clang\r\n" +
            $"clang -std=c99 -O2 -o {n}.exe {n}.c -lm\r\n" +
            "if errorlevel 1 (echo Build failed & endlocal & exit /b 1)\r\n" +
            $"echo Built: {n}.exe\r\n" +
            "endlocal\r\n" +
            "exit /b 0\r\n";

        var makefile =
            "CC ?= cc\n" +
            "CFLAGS = -std=c99 -Wall -O2\n\n" +
            $"{n}: {n}.c prolang_runtime.h\n" +
            $"\t$(CC) $(CFLAGS) -o $@ $< -lm\n\n" +
            "clean:\n" +
            $"\trm -f {n} {n}.exe\n\n" +
            ".PHONY: clean\n";

        var buildShPath = Path.Combine(outputDir, "build.sh");
        File.WriteAllText(buildShPath, buildSh);
        try
        {
#pragma warning disable CA1416
            File.SetUnixFileMode(buildShPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
        }
        catch { /* not on POSIX */ }
        File.WriteAllText(Path.Combine(outputDir, "build.bat"), buildBat, System.Text.Encoding.ASCII);
        File.WriteAllText(Path.Combine(outputDir, "Makefile"), makefile);

        return emitDiagnostics;
    }

    public ImmutableArray<Diagnostic> EmitPsp(string moduleName, string outputDir)
    {
        // Bind + diagnose (mirror EmitC but without emitting its own main entry point)
        if (_importDiagnostics.Any()) return _importDiagnostics;

        var parseDiagnostics = SyntaxTrees.SelectMany(st => st.Diagnostics);
        var diagnostics = parseDiagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();
        if (diagnostics.Any()) return diagnostics;

        var program = GetProgram();
        if (program.Diagnostics.Any()) return program.Diagnostics;

        Directory.CreateDirectory(outputDir);

        var cFile = Path.Combine(outputDir, $"{moduleName}.c");
        var emitDiagnostics = CEmitter.Emit(program, moduleName, cFile, emitMainEntry: false);

        // Copy runtime headers (base.h, prl_*.h, prolang_runtime.h)
        CRuntimeHeader.WriteAll(outputDir);

        // Step 2: write PSP wrapper (psp_main.c)
        var pspMain = 
            "/* PSP wrapper - generated by ProLang */\n" +
            "#ifndef __PSP__\n" +
            "#error This file must be compiled with -D__PSP__ using the pspdev toolchain\n" +
            "#endif\n" +
            "\n" +
            "#include <pspkernel.h>\n" +
            "#include <pspdebug.h>\n" +
            "#include <pspdisplay.h>\n" +
            "#include <pspctrl.h>\n" +
            "\n" +
            $"PSP_MODULE_INFO(\"{moduleName}\", PSP_MODULE_USER, 1, 0);\n" +
            "PSP_MAIN_THREAD_ATTR(THREAD_ATTR_USER);\n" +
            "PSP_HEAP_SIZE_KB(8192);\n" +
            "\n" +
            "static volatile int g_running = 1;\n" +
            "\n" +
            "static int exit_cb(int a, int b, void *c) { g_running = 0; return 0; }\n" +
            "\n" +
            "static int cb_thread(SceSize a, void *b) {\n" +
            "    int id = sceKernelCreateCallback(\"Exit\", exit_cb, NULL);\n" +
            "    sceKernelRegisterExitCallback(id);\n" +
            "    sceKernelSleepThreadCB();\n" +
            "    return 0;\n" +
            "}\n" +
            "\n" +
            "// Forward declaration of ProLang entry function\n" +
            $"extern void prl___UserMain(void);\n" +
            "\n" +
            "int main(int argc, char *argv[]) {\n" +
            "    (void)argc; (void)argv;\n" +
            "    int thid = sceKernelCreateThread(\"cb\", cb_thread, 0x11, 0xFA0, 0, 0);\n" +
            "    if (thid >= 0) sceKernelStartThread(thid, 0, NULL);\n" +
            "\n" +
            "    sceCtrlSetSamplingMode(PSP_CTRL_MODE_ANALOG);\n" +
            "\n" +
            "    prl___UserMain();\n" +
            "\n" +
            "    sceKernelExitGame();\n" +
            "    return 0;\n" +
            "}\n";

        File.WriteAllText(Path.Combine(outputDir, "psp_main.c"), pspMain);

        // Step 3: write PSP Makefile
        var pspMakefile =
            $"TARGET = {moduleName}\n" +
            "OBJS = psp_main.o " + moduleName + ".o\n" +
            "CFLAGS = -O2 -G0 -Wall -D__PSP__\n" +
            "LIBS = -lpspdebug -lpspgu -lpspgum -lpspge -lpspdisplay -lpspctrl -lm -lpspuser -lc\n" +
            "\n" +
            "EXTRA_TARGETS = EBOOT.PBP\n" +
            $"PSP_EBOOT_TITLE = {moduleName}\n" +
            "\n" +
            "PSPSDK = $(shell psp-config --pspsdk-path)\n" +
            "include $(PSPSDK)/lib/build.mak\n";

        File.WriteAllText(Path.Combine(outputDir, "Makefile.psp"), pspMakefile);

        return emitDiagnostics;
    }

    public ImmutableArray<Diagnostic> Emit(string moduleName, string[] references, string outputPath)
    {
        if (_importDiagnostics.Any())
        {
            return _importDiagnostics;
        }

        var parseDiagnostics = SyntaxTrees.SelectMany(st => st.Diagnostics);
        var diagnostics = parseDiagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();
        if (diagnostics.Any())
        {
            return diagnostics;
        }

        var program = GetProgram();

        if (program.Diagnostics.Any())
        {
            return program.Diagnostics;
        }

        var emitDiagnostics = Emitter.Emit(program, moduleName, references, outputPath);

        // On success, copy any stdlib native assemblies (lib/ DLLs) next to the output
        // so the compiled program can resolve them at runtime without a manual deploy step.
        if (!emitDiagnostics.Any())
        {
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (outputDir != null)
            {
                foreach (var libPath in _libAssemblyPaths)
                {
                    var dest = Path.Combine(outputDir, Path.GetFileName(libPath));
                    try
                    {
                        File.Copy(libPath, dest, overwrite: true);
                    }
                    catch
                    {
                        // Non-fatal: the compiled program may still run if the DLL
                        // is already present from a previous build.
                    }
                }
            }
        }

        return emitDiagnostics;
    }

}