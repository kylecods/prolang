using System.Collections.Immutable;
using System.Reflection;
using ProLang.Symbols;
using ProLang.Symbols.Modules;

namespace ProLang.Interop;

/// <summary>
/// Represents a .NET namespace as a ProLang module. Wraps static methods, properties, 
/// and constructors from types in a .NET namespace.
/// </summary>
public sealed class DotNetInteropModule : BuiltInModule
{
    private readonly string _namespaceName;
    private readonly Lazy<IReadOnlyList<FunctionSymbol>> _functions;
    private readonly Lazy<IReadOnlyList<TypeSymbol>> _types;

    public DotNetInteropModule(string namespaceName)
    {
        _namespaceName = namespaceName;
        _functions = new Lazy<IReadOnlyList<FunctionSymbol>>(LoadFunctions);
        _types = new Lazy<IReadOnlyList<TypeSymbol>>(LoadTypes);
    }

    public override string Name => _namespaceName;

    public override IReadOnlyList<FunctionSymbol> Functions => _functions.Value;

    public IReadOnlyList<TypeSymbol> Types => _types.Value;

    /// <summary>
    /// Gets all types in this namespace.
    /// </summary>
    public IEnumerable<Type> GetDotNetTypes()
    {
        return DotNetAssemblyRegistry.Instance.GetTypesInNamespace(_namespaceName);
    }

    private IReadOnlyList<FunctionSymbol> LoadFunctions()
    {
        var functions = new List<FunctionSymbol>();
        var registry = DotNetAssemblyRegistry.Instance;

        foreach (var type in GetDotNetTypes())
        {
            // Skip non-public types
            if (!type.IsPublic)
                continue;

            // Skip enums (could add support later)
            if (type.IsEnum)
                continue;

            // Add static methods
            foreach (var method in registry.GetStaticMethods(type))
            {
                // Use fully qualified name: TypeName.MethodName
                var funcName = $"{type.Name}.{method.Name}";
                var func = DotNetFunctionSymbol.FromStaticMethod(method);

                // Create a new symbol with the qualified name
                var qualifiedFunc = new DotNetFunctionSymbol(
                    funcName,
                    func.Parameters,
                    func.Type,
                    func.MethodInfo,
                    func.ConstructorInfo,
                    func.DeclaringType,
                    func.IsStatic);

                functions.Add(qualifiedFunc);
            }

            // Add static properties
            foreach (var property in registry.GetStaticProperties(type))
            {
                var funcName = $"{type.Name}.{property.Name}";
                var func = DotNetFunctionSymbol.FromStaticProperty(property);

                var qualifiedFunc = new DotNetFunctionSymbol(
                    funcName,
                    func.Parameters,
                    func.Type,
                    func.MethodInfo,
                    func.ConstructorInfo,
                    func.DeclaringType,
                    func.IsStatic);

                functions.Add(qualifiedFunc);
            }

            // Add static fields (constants, etc.)
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var funcName = $"{type.Name}.{field.Name}";
                var func = DotNetFunctionSymbol.FromStaticField(field);

                var qualifiedFunc = new DotNetFunctionSymbol(
                    funcName,
                    func.Parameters,
                    func.Type,
                    func.MethodInfo,
                    func.ConstructorInfo,
                    func.DeclaringType,
                    func.IsStatic);

                functions.Add(qualifiedFunc);
            }

            // Add constructors as "new TypeName"
            foreach (var constructor in DotNetAssemblyRegistry.GetConstructors(type))
            {
                var funcName = $"{type.Name}.new";
                var func = DotNetFunctionSymbol.FromConstructor(constructor);

                var qualifiedFunc = new DotNetFunctionSymbol(
                    funcName,
                    func.Parameters,
                    func.Type,
                    func.MethodInfo,
                    func.ConstructorInfo,
                    func.DeclaringType,
                    func.IsStatic);

                functions.Add(qualifiedFunc);
            }
        }

        return functions;
    }

    private IReadOnlyList<TypeSymbol> LoadTypes()
    {
        var types = new List<TypeSymbol>();

        foreach (var type in GetDotNetTypes())
        {
            if (type.IsPublic)
            {
                types.Add(new TypeSymbol(type.Name));
            }
        }

        return types;
    }

    /// <summary>
    /// Tries to resolve a type by name within this namespace.
    /// </summary>
    public Type? TryResolveType(string typeName)
    {
        return DotNetAssemblyRegistry.Instance.FindTypeByNamespace(_namespaceName, typeName);
    }

    /// <summary>
    /// Tries to resolve a function by qualified name (e.g., "Console.WriteLine").
    /// </summary>
    public DotNetFunctionSymbol? TryResolveFunction(string qualifiedName)
    {
        return Functions.OfType<DotNetFunctionSymbol>()
            .FirstOrDefault(f => f.Name.Equals(qualifiedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a module for a specific type, exposing all its static members.
    /// </summary>
    public static DotNetInteropModule? CreateForType(Type type)
    {
        if (!type.IsPublic)
            return null;

        var module = new DotNetInteropModule(type.FullName!);
        return module;
    }

    public override string ToString()
    {
        return $"dotnet namespace {_namespaceName} ({Functions.Count} members)";
    }
}
