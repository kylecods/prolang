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