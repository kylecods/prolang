using System.Collections.Concurrent;
using System.Reflection;

namespace ProLang.Interop;

/// <summary>
/// Central registry for .NET assemblies available to the binder. Provides caching and lookup for
/// types, methods, and properties. Assemblies are read as <em>metadata only</em> through a
/// <see cref="MetadataLoadContext"/> — nothing is loaded for execution, no code runs, and the
/// compiler's own process is never exposed to anything an imported assembly contains.
/// </summary>
/// <remarks>
/// <para>
/// This used to load assemblies with <see cref="Assembly.LoadFrom"/> into the default load context,
/// which made the compiler incompatible with Native AOT: AOT binaries cannot JIT-load arbitrary
/// managed assemblies, and reflection over runtime types requires trimming annotations that cannot
/// be provided for assemblies the compiler has never seen. <see cref="MetadataLoadContext"/> reads
/// the same files with the same <c>Type</c>/<c>MethodInfo</c>/<c>ConstructorInfo</c> API surface,
/// so every consumer — the binder, the interop modules, the type mapper — is unchanged, while the
/// whole thing becomes pure metadata reading that works identically under AOT.
/// </para>
/// <para>
/// The one behavioural difference: metadata-only types cannot be instantiated or invoked. That is
/// correct for a compiler — it only ever needs signatures to bind against and emit calls to — and
/// <see cref="DotNetFunctionSymbol.Invoke"/> now reports that rather than pretending otherwise.
/// </para>
/// </remarks>
public sealed class DotNetAssemblyRegistry
{
    private static readonly Lazy<DotNetAssemblyRegistry> _instance = new(() => new DotNetAssemblyRegistry());
    public static DotNetAssemblyRegistry Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type[]> _typeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type?> _typeLookupCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type?> _simpleNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, MethodInfo[]> _staticMethodCache = new();
    private readonly ConcurrentDictionary<Type, PropertyInfo[]> _staticPropertyCache = new();
    private readonly HashSet<string> _runtimeAssemblyPaths;
    private readonly object _contextGate = new();
    private MetadataLoadContext? _metadataContext;

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

        // Current runtime directory (implementation assemblies — safe to read as metadata)
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        if (Directory.Exists(runtimeDir))
        {
            foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
            {
                paths.Add(dll);
            }
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
        // Reference assemblies carry no method bodies at all; they are used only for type discovery.
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
                // shadow the real implementations when both are visible.
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

        // A MetadataLoadContext's PathAssemblyResolver requires that each assembly simple name map
        // to exactly one path. The runtime directory and the ref packs both carry mscorlib.dll and
        // friends, so the set above can hold two files with the same name. The runtime directory
        // was added first and holds the real implementations, so it wins; a later duplicate is
        // dropped rather than left to make the resolver throw.
        var bySimpleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            bySimpleName.TryAdd(Path.GetFileName(path), path);
        }

        return new HashSet<string>(bySimpleName.Values, StringComparer.OrdinalIgnoreCase);
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

        // First try among the discovered runtime paths — this covers both the core framework
        // directory and the Windows Desktop one, where System.Windows.Forms lives.
        var dllName = assemblyName + ".dll";
        foreach (var path in _runtimeAssemblyPaths)
        {
            if (Path.GetFileName(path).Equals(dllName, StringComparison.OrdinalIgnoreCase))
            {
                var assembly = LoadMetadataOnly(path);
                if (assembly != null)
                {
                    _loadedAssemblies[assemblyName] = assembly;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Loads an assembly from a file path. Works with C#, F#, VB.NET, and any .NET assembly.
    /// The assembly is read as metadata only — its code is never executed by the compiler.
    /// </summary>
    public Assembly? LoadAssembly(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (_loadedAssemblies.TryGetValue(fullPath, out var cached))
            return cached;

        if (!File.Exists(fullPath))
            return null;

        var assembly = LoadMetadataOnly(fullPath);
        if (assembly == null)
            return null;

        _loadedAssemblies[fullPath] = assembly;
        _loadedAssemblies[assembly.GetName().Name!] = assembly;
        return assembly;
    }

    /// <summary>
    /// Gets or creates the shared metadata-only load context.
    /// </summary>
    /// <remarks>
    /// The context's resolver falls back to every discovered runtime assembly path, so a type
    /// referenced by one assembly resolves against another without either being "loaded". Created
    /// lazily on first use and kept for the process lifetime: the runtime assemblies cannot change
    /// while the compiler is running, and sharing one context is what lets types from different
    /// assemblies compare equal when they name the same file.
    /// </remarks>
    private MetadataLoadContext GetMetadataContext()
    {
        lock (_contextGate)
        {
            if (_metadataContext != null)
            {
                return _metadataContext;
            }

            var resolver = new PathAssemblyResolver(_runtimeAssemblyPaths);
            _metadataContext = new MetadataLoadContext(resolver);
            return _metadataContext;
        }
    }

    /// <summary>
    /// Reads an assembly from disk into the metadata context.
    /// </summary>
    /// <returns>The metadata-only <see cref="Assembly"/>, or null if unreadable.</returns>
    private Assembly? LoadMetadataOnly(string fullPath)
    {
        try
        {
            return GetMetadataContext().LoadFromAssemblyPath(fullPath);
        }
        catch
        {
            // Not a readable managed assembly, or a dependency could not be resolved.
            return null;
        }
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

        // Search all loaded assemblies
        foreach (var assembly in _loadedAssemblies.Values.Distinct())
        {
            var type = assembly.GetType(fullName);
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
        if (_staticMethodCache.TryGetValue(type, out var cached))
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

        _staticMethodCache[type] = methods;
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
        if (_staticPropertyCache.TryGetValue(type, out var cached))
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

        _staticPropertyCache[type] = props;
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
