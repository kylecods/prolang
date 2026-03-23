using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interpreter;
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
 

    private ProLangCompilation(bool isScript, ProLangCompilation? previous, ImmutableArray<Diagnostic> importDiagnostics, ImmutableHashSet<string> importedModules, params SyntaxTree[] syntaxTrees)
    {
        IsScript = isScript;
        Previous = previous;
        SyntaxTrees = syntaxTrees.ToImmutableArray();
        _importDiagnostics = importDiagnostics;
        _importedModules = importedModules;
    }

    public static ProLangCompilation Create(params SyntaxTree[] syntaxTrees)
    {
        var (resolved, diagnostics, importedModules) = ResolveAllImports(syntaxTrees.ToImmutableArray());
        return new ProLangCompilation(isScript:false, previous: null, diagnostics, importedModules, resolved.ToArray());
    }

    public static ProLangCompilation CreateScript(ProLangCompilation previous, params SyntaxTree[] syntaxTrees)
    {
        var (resolved, diagnostics, importedModules) = ResolveAllImports(syntaxTrees.ToImmutableArray());

        // Also include built-in modules
        var allModules = BuiltInModule.GetAll().Select(m => m.Name)
            .Concat(importedModules)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProLangCompilation(isScript: true, previous: previous, diagnostics, allModules, resolved.ToArray());
    }

    private static (ImmutableArray<SyntaxTree> Trees, ImmutableArray<Diagnostic> Diagnostics, ImmutableHashSet<string> ImportedModules) ResolveAllImports(
        ImmutableArray<SyntaxTree> syntaxTrees)
    {
        var allTrees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    var namespaceName = importPath.Substring("dotnet:".Length);
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
                    var assemblyPath = importPath.Substring("assembly:".Length);

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

        return (allTrees.ToImmutable(), diagnostics.ToImmutable(), importedModules.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Registers all public namespaces from an assembly as importable modules.
    /// </summary>
    private static void RegisterAssemblyNamespaces(System.Reflection.Assembly assembly)
    {
        try
        {
            var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in assembly.GetExportedTypes())
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

    internal EvaluationResult Evaluate(Dictionary<VariableSymbol, object> variables)
    {
        if (_importDiagnostics.Any())
        {
            return new EvaluationResult(_importDiagnostics, null!);
        }

        var parseDiagnostics = SyntaxTrees.SelectMany(st => st.Diagnostics);

        var diagnostics = parseDiagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();
        
        if (diagnostics.Any())
        { 
            return new EvaluationResult(diagnostics, null!);
        }

        var program = GetProgram();

        if (program.Diagnostics.Any())
        {
            return new EvaluationResult(program.Diagnostics.ToImmutableArray(), null!);
        }

        var evaluator = new Evaluator(program, variables);
        
        var value = evaluator.Evaluate();

        return new EvaluationResult(ImmutableArray<Diagnostic>.Empty, value);

    }
    
    public void EmitTree(TextWriter writer)
    {
        if (GlobalScope.MainFunction != null) 
        { 
            EmitTree(GlobalScope.MainFunction,writer);
        }
        else if (GlobalScope.ScriptFunction != null)
        {
           EmitTree(GlobalScope.ScriptFunction,writer);
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
            return program.Diagnostics.ToImmutableArray();
        }

        return Emitter.Emit(program, moduleName, references, outputPath);
    }

}