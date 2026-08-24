using System.Reflection;
using ProLang.Interop;

namespace ProLang.Symbols.Modules;

public abstract class BuiltInModule
{
    private static readonly Dictionary<string, BuiltInModule> Registry = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    static BuiltInModule()
    {
        Register(new IOModule());
        Register(new MathModule());
        Register(new FileSystemModule());
        Register(new ArrayModule());
        Register(new ConsoleModule());
        Register(new PspModule());
        Register(new TestModule());
    }

    public static void Register(BuiltInModule module)
    {
        Registry[module.Name] = module;
    }

    public abstract string Name { get; }

    /// <summary>
    /// Whether this is one of the language's own modules, rather than a .NET namespace or assembly
    /// registered by an import.
    /// </summary>
    /// <remarks>
    /// The registry holds both, and they are not interchangeable: the intrinsic modules are a
    /// fixed, documented set that belongs in a completion list, while the interop ones appear and
    /// disappear according to what some file somewhere imported. Telling them apart by the shape
    /// of their name does not work — an interop module's <see cref="Name"/> is the bare namespace,
    /// and only its registry key carries the <c>dotnet:</c> prefix.
    /// </remarks>
    public virtual bool IsIntrinsic => true;

    /// <summary>
    /// One line saying what this module is for.
    /// </summary>
    /// <remarks>
    /// Shown beside the module's name while someone types an import, which is the moment they are
    /// choosing between modules and the only moment the answer is useful.
    /// </remarks>
    public virtual string Summary => string.Empty;

    public abstract IReadOnlyList<FunctionSymbol> Functions { get; }

    public static bool TryGetModule(string name, out BuiltInModule? module)
    {
        return Registry.TryGetValue(name, out module);
    }

    public static IEnumerable<BuiltInModule> GetAll()
    {
        return Registry.Values;
    }

    /// <summary>The language's own modules: <c>io</c>, <c>math</c>, <c>fs</c> and the rest.</summary>
    public static IEnumerable<BuiltInModule> GetIntrinsic()
    {
        return Registry.Values.Where(m => m.IsIntrinsic);
    }

    public static IEnumerable<FunctionSymbol> GetAllFunctions()
    {
        return Registry.Values.SelectMany(m => m.Functions);
    }

    /// <summary>
    /// The module a builtin belongs to, or null if the symbol is not a builtin.
    /// </summary>
    /// <remarks>
    /// By instance, never by name: <c>length</c> is a builtin twice over, once for arrays and once
    /// for strings, and <c>adding-a-builtin.md</c> makes the same point about dispatch. It answers
    /// two questions an editor asks — which import a name needs, and whether a name came from the
    /// compiler at all.
    /// </remarks>
    public static string? ModuleOf(FunctionSymbol function)
    {
        foreach (var (importPath, module) in Registry)
        {
            foreach (var candidate in module.Functions)
            {
                if (ReferenceEquals(candidate, function))
                {
                    // The registry key, not the module's name: for an interop module they differ,
                    // and the key is the string a program writes in its import — which is what the
                    // caller is going to compare against or tell the user to type.
                    return importPath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a .NET namespace as a module.
    /// Prefix should be "dotnet:" followed by the namespace.
    /// </summary>
    public static bool RegisterDotNetNamespace(string namespaceName)
    {
        var registry = DotNetAssemblyRegistry.Instance;

        if (!registry.NamespaceExists(namespaceName))
            return false;

        var module = new DotNetInteropModule(namespaceName);
        Registry[$"dotnet:{namespaceName}"] = module;

        return true;
    }

    /// <summary>
    /// Loads a .NET assembly from a file path and registers it.
    /// Prefix should be "assembly:" followed by the file path.
    /// </summary>
    public static Assembly? LoadAssemblyFromFile(string filePath)
    {
        var registry = DotNetAssemblyRegistry.Instance;
        var assembly = registry.LoadAssembly(filePath);

        if (assembly != null)
        {
            _loadedAssemblies[filePath] = assembly;
        }

        return assembly;
    }

    /// <summary>
    /// Gets all loaded external .NET assemblies.
    /// </summary>
    public static IReadOnlyDictionary<string, Assembly> GetLoadedAssemblies()
    {
        return _loadedAssemblies;
    }

    /// <summary>
    /// Clears dynamically registered modules (for testing).
    /// </summary>
    internal static void ClearDynamicModules()
    {
        var dynamicKeys = Registry.Keys
            .Where(k => k.StartsWith("dotnet:") || k.StartsWith("assembly:"))
            .ToList();

        foreach (var key in dynamicKeys)
        {
            Registry.Remove(key);
        }
    }
}
