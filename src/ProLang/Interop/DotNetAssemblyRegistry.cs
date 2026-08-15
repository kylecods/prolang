using System.Collections.Concurrent;
using System.Reflection;

namespace ProLang.Interop;

/// <summary>
/// Central registry for loaded .NET assemblies. Provides caching and lookup for types, methods, and properties.
/// Supports loading assemblies from file paths or by name from the runtime.
/// </summary>
public sealed class DotNetAssemblyRegistry
{
    private static readonly Lazy<DotNetAssemblyRegistry> _instance = new(() => new DotNetAssemblyRegistry());
    public static DotNetAssemblyRegistry Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type[]> _typeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type?> _typeLookupCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type?> _simpleNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<RuntimeTypeHandle, MethodInfo[]> _staticMethodCache = new();
    private readonly ConcurrentDictionary<RuntimeTypeHandle, PropertyInfo[]> _staticPropertyCache = new();
    private readonly HashSet<string> _runtimeAssemblyPaths;

    private DotNetAssemblyRegistry()
    {
        _runtimeAssemblyPaths = DiscoverRuntimeAssemblies();
        PreloadCoreAssemblies();
    }

    /// <summary>
    /// Discovers available runtime assemblies from the .NET SDK and runtime directories.
    /// </summary>
    // Paths to SDK refpack assemblies (for compile-time reference, not runtime loading)
    private List<string> _refpackPaths = new();

    private HashSet<string> DiscoverRuntimeAssemblies()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Current runtime directory (implementation assemblies — safe to load)
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            paths.Add(dll);
        }

        // Windows Desktop shared runtime directories (implementation assemblies for WinForms/WPF)
        // Must be in _runtimeAssemblyPaths so TryLoadRuntimeAssembly can find System.Windows.Forms.
        foreach (var sharedRoot in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "shared", "Microsoft.WindowsDesktop.App"),
        })
        {
            if (!Directory.Exists(sharedRoot)) continue;
            // Sort version directories numerically so 10.x beats 9.x
            var latestVersionDir = Directory.GetDirectories(sharedRoot)
                .OrderByDescending(d => ParseVersion(Path.GetFileName(d)))
                .FirstOrDefault();
            if (latestVersionDir != null)
            {
                foreach (var dll in Directory.GetFiles(latestVersionDir, "*.dll"))
                {
                    paths.Add(dll);
                }
            }
        }

        // Collect refpack reference assemblies into a separate list (NOT in _runtimeAssemblyPaths).
        // Reference assemblies cannot be loaded for execution — they are used only for type discovery.
        foreach (var packsRoot in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "packs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "packs"),
        })
        {
            if (!Directory.Exists(packsRoot)) continue;
            foreach (var packDir in Directory.GetDirectories(packsRoot))
            {
                var packName = Path.GetFileName(packDir);
                // Skip Windows Desktop refpacks — they contain reference-only assemblies that
                // poison the load context when attempted (causing subsequent real-assembly loads to fail).
                if (packName.StartsWith("Microsoft.WindowsDesktop.App.Ref", StringComparison.OrdinalIgnoreCase))
                    continue;
                var refDirs = Directory.GetDirectories(packDir, "ref", SearchOption.AllDirectories);
                foreach (var refDir in refDirs)
                {
                    foreach (var netDir in Directory.GetDirectories(refDir))
                    {
                        foreach (var dll in Directory.GetFiles(netDir, "*.dll"))
                        {
                            paths.Add(dll);
                        }
                    }
                }
            }
        }

        return paths;
    }

    private static Version ParseVersion(string dirName)
    {
        return Version.TryParse(dirName, out var v) ? v : new Version(0, 0);
    }

    /// <summary>
    /// Preloads essential .NET assemblies that are commonly used.
    /// </summary>
    private void PreloadCoreAssemblies()
    {
        var coreAssemblyNames = new[]
        {
            "System.Runtime",
            "System.Console",
            "System.Collections",
            "System.Collections.Concurrent",
            "System.Linq",
            "System.Text.RegularExpressions",
            "System.IO.FileSystem",
            "System.Threading",
            "System.Net.Primitives",
            "System.Net.Http",
            "System.Text.Json",
            "System.Xml.ReaderWriter",
            "System.Data.Common",
            "Microsoft.CSharp",
            // Windows Desktop — silently skipped when not installed (Linux/Mac)
            "System.Windows.Forms",
            "System.Drawing.Common",
            "System.Drawing.Primitives",
        };

        foreach (var name in coreAssemblyNames)
        {
            TryLoadRuntimeAssembly(name);
        }
    }

    /// <summary>
    /// Tries to load a runtime assembly by its simple name (e.g., "System.Text.Json").
    /// </summary>
    public bool TryLoadRuntimeAssembly(string assemblyName)
    {
        if (_loadedAssemblies.ContainsKey(assemblyName))
            return true;

        try
        {
            // First try to load from the runtime
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            _loadedAssemblies[assemblyName] = assembly;
            return true;
        }
        catch
        {
            // Try to find it in discovered paths
            var dllName = assemblyName + ".dll";
            foreach (var path in _runtimeAssemblyPaths)
            {
                if (Path.GetFileName(path).Equals(dllName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(path);
                        _loadedAssemblies[assemblyName] = assembly;
                        return true;
                    }
                    catch
                    {
                        // Ignore load failures
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Loads an assembly from a file path. Works with C#, F#, VB.NET, and any .NET assembly.
    /// Registers an AssemblyResolve handler so that the loaded assembly's dependencies
    /// (e.g., System.Windows.Forms) can be found via the discovered runtime assembly paths.
    /// </summary>
    public Assembly? LoadAssembly(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (_loadedAssemblies.TryGetValue(fullPath, out var cached))
            return cached;

        if (!File.Exists(fullPath))
            return null;

        // Ensure the AssemblyResolve hook is installed once, so transitive dependencies
        // of the loaded assembly can be resolved from our discovered runtime paths.
        EnsureAssemblyResolveHook();

        try
        {
            var assembly = Assembly.LoadFrom(fullPath);
            _loadedAssemblies[fullPath] = assembly;
            _loadedAssemblies[assembly.GetName().Name!] = assembly;
            return assembly;
        }
        catch
        {
            return null;
        }
    }

    private bool _assemblyResolveHookInstalled;

    private void EnsureAssemblyResolveHook()
    {
        if (_assemblyResolveHookInstalled) return;
        _assemblyResolveHookInstalled = true;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name == null) return null;

            // Return already-loaded assembly if available
            if (_loadedAssemblies.TryGetValue(name, out var loaded)) return loaded;

            // Search our discovered runtime paths for the DLL
            var dllName = name + ".dll";
            foreach (var path in _runtimeAssemblyPaths)
            {
                if (Path.GetFileName(path).Equals(dllName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var asm = Assembly.LoadFrom(path);
                        _loadedAssemblies[name] = asm;
                        return asm;
                    }
                    catch { }
                }
            }

            return null;
        };
    }

    /// <summary>
    /// Gets a loaded assembly by name.
    /// </summary>
    public Assembly? GetAssembly(string name)
    {
        _loadedAssemblies.TryGetValue(name, out var assembly);
        return assembly;
    }

    /// <summary>
    /// Gets all loaded assemblies.
    /// </summary>
    public IReadOnlyCollection<Assembly> GetLoadedAssemblies()
    {
        return _loadedAssemblies.Values.Distinct().ToList();
    }

    /// <summary>
    /// Finds a type by its full name across all loaded assemblies.
    /// </summary>
    public Type? FindType(string fullName)
    {
        var cacheKey = fullName;
        if (_typeLookupCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Check mscorlib/System.Private.CoreLib first for common types
        var type = Type.GetType(fullName);
        if (type != null)
        {
            _typeLookupCache[cacheKey] = type;
            return type;
        }

        // Search all loaded assemblies
        foreach (var assembly in _loadedAssemblies.Values.Distinct())
        {
            type = assembly.GetType(fullName);
            if (type != null)
            {
                _typeLookupCache[cacheKey] = type;
                return type;
            }
        }

        _typeLookupCache[cacheKey] = null;
        return null;
    }

    /// <summary>
    /// Finds a type by searching with namespace prefix (e.g., "System.Text.Json.JsonSerializer").
    /// </summary>
    public Type? FindTypeByNamespace(string namespacePrefix, string typeName)
    {
        var fullName = $"{namespacePrefix}.{typeName}";
        return FindType(fullName);
    }

    /// <summary>
    /// Finds a public type by simple name (case-insensitive) across all loaded assemblies.
    /// Results are cached so subsequent lookups for the same name are O(1).
    /// </summary>
    public Type? FindTypeBySimpleName(string simpleName)
    {
        if (_simpleNameCache.TryGetValue(simpleName, out var cached))
            return cached;

        Type? found = null;
        foreach (var assembly in _loadedAssemblies.Values.Distinct())
        {
            try
            {
                found = assembly.GetTypes().FirstOrDefault(t =>
                    t.IsPublic && t.Name.Equals(simpleName, StringComparison.OrdinalIgnoreCase));
                if (found != null) break;
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Some types couldn't be loaded due to missing dependencies; search partial results
                found = ex.Types.FirstOrDefault(t =>
                    t != null && t.IsPublic && t.Name.Equals(simpleName, StringComparison.OrdinalIgnoreCase));
                if (found != null) break;
            }
            catch { }
        }

        _simpleNameCache[simpleName] = found;
        return found;
    }

    /// <summary>
    /// Gets all public types in a namespace across all loaded assemblies.
    /// </summary>
    public IEnumerable<Type> GetTypesInNamespace(string namespaceName)
    {
        var cacheKey = $"ns:{namespaceName}";
        if (_typeCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var types = new List<Type>();
        foreach (var assembly in _loadedAssemblies.Values.Distinct())
        {
            try
            {
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.Namespace != null &&
                        type.Namespace.Equals(namespaceName, StringComparison.OrdinalIgnoreCase))
                    {
                        types.Add(type);
                    }
                }
            }
            catch
            {
                // Some assemblies may throw on GetExportedTypes
            }
        }

        _typeCache[cacheKey] = types.ToArray();
        return types;
    }

    /// <summary>
    /// Gets all public static methods of a type that are callable.
    /// </summary>
    public IReadOnlyList<MethodInfo> GetStaticMethods(Type type)
    {
        if (_staticMethodCache.TryGetValue(type.TypeHandle, out var cached))
            return cached;

        MethodInfo[] methods;
        try
        {
            methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => !m.IsSpecialName && !m.IsGenericMethod)
                .ToArray();
        }
        catch
        {
            methods = Array.Empty<MethodInfo>();
        }

        _staticMethodCache[type.TypeHandle] = methods;
        return methods;
    }

    /// <summary>
    /// Gets all public instance methods of a type that are callable.
    /// </summary>
    public IReadOnlyList<MethodInfo> GetInstanceMethods(Type type)
    {
        try
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && !m.IsGenericMethod && m.DeclaringType != typeof(object))
                .ToList();
        }
        catch
        {
            return Array.Empty<MethodInfo>();
        }
    }

    /// <summary>
    /// Gets all public static properties of a type.
    /// </summary>
    public IReadOnlyList<PropertyInfo> GetStaticProperties(Type type)
    {
        if (_staticPropertyCache.TryGetValue(type.TypeHandle, out var cached))
            return cached;

        PropertyInfo[] props;
        try
        {
            props = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.CanRead)
                .ToArray();
        }
        catch
        {
            props = [];
        }

        _staticPropertyCache[type.TypeHandle] = props;
        return props;
    }

    /// <summary>
    /// Gets all public constructors of a type.
    /// </summary>
    public static IReadOnlyList<ConstructorInfo> GetConstructors(Type type)
    {
        try
        {
            return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Gets all available namespaces from loaded assemblies.
    /// </summary>
    public IEnumerable<string> GetAvailableNamespaces()
    {
        var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in _loadedAssemblies.Values.Distinct())
        {
            try
            {
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!string.IsNullOrEmpty(type.Namespace))
                    {
                        namespaces.Add(type.Namespace);
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        return namespaces.OrderBy(n => n);
    }

    /// <summary>
    /// Checks if a namespace exists in any loaded assembly.
    /// </summary>
    public bool NamespaceExists(string namespaceName)
    {
        foreach (var assembly in _loadedAssemblies.Values.Distinct())
        {
            try
            {
                if (assembly.GetExportedTypes().Any(t =>
                        t.Namespace?.Equals(namespaceName, StringComparison.OrdinalIgnoreCase) == true))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore
            }
        }

        return false;
    }
}
