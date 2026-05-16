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
    }

    public static void Register(BuiltInModule module)
    {
        Registry[module.Name] = module;
    }

    public abstract string Name { get; }

    public abstract IReadOnlyList<FunctionSymbol> Functions { get; }

    public static bool TryGetModule(string name, out BuiltInModule? module)
    {
        return Registry.TryGetValue(name, out module);
    }

    public static IEnumerable<BuiltInModule> GetAll()
    {
        return Registry.Values;
    }

    public static IEnumerable<FunctionSymbol> GetAllFunctions()
    {
        return Registry.Values.SelectMany(m => m.Functions);
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
