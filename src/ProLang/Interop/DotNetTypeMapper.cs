using System.Collections.Concurrent;
using System.Reflection;
using ProLang.Symbols;

namespace ProLang.Interop;

/// <summary>
/// Maps .NET types to ProLang type symbols and provides conversion utilities.
/// </summary>
public static class DotNetTypeMapper
{
    private static readonly ConcurrentDictionary<Type, TypeSymbol> _typeMap = new();
    private static readonly ConcurrentDictionary<TypeSymbol, Type> _reverseMap = new();

    static DotNetTypeMapper()
    {
        RegisterPrimitiveMapping(typeof(void), TypeSymbol.Void);
        RegisterPrimitiveMapping(typeof(bool), TypeSymbol.Bool);
        RegisterPrimitiveMapping(typeof(byte), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(sbyte), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(short), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(ushort), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(int), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(uint), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(long), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(ulong), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(float), TypeSymbol.Float32);
        RegisterPrimitiveMapping(typeof(double), TypeSymbol.Float64);
        RegisterPrimitiveMapping(typeof(decimal), TypeSymbol.Float64);
        RegisterPrimitiveMapping(typeof(string), TypeSymbol.String);
        RegisterPrimitiveMapping(typeof(char), TypeSymbol.String);

        // WinForms / Drawing value types that map one-to-one to prolang int
        // System.Drawing.Color   → int (ARGB): Color.FromArgb(int) / Color.ToArgb()
        // All .NET enums         → int (underlying type is always int)
        TryRegisterDrawingTypes();
    }

    private static void TryRegisterDrawingTypes()
    {
        // Metadata-only assemblies are not visible to Type.GetType(string), which searches the
        // executing context only — so the Color type is looked up through the registry instead.
        var registry = DotNetAssemblyRegistry.Instance;
        var colorType = registry.FindType("System.Drawing.Color");
        if (colorType != null)
            _typeMap[colorType] = TypeSymbol.Int;
    }

    private static void RegisterPrimitiveMapping(Type clrType, TypeSymbol proLangType)
    {
        _typeMap[clrType] = proLangType;
        _reverseMap[proLangType] = clrType;
    }

    /// <summary>
    /// Maps a .NET type to the corresponding ProLang type symbol.
    /// Returns TypeSymbol.Any for types that don't have direct mappings.
    /// </summary>
    public static TypeSymbol MapToProLangType(Type dotNetType)
    {
        // Metadata-only types are distinct Type instances from the compiler's own typeof(int),
        // so the reference-keyed _typeMap misses them. Match primitives by full name instead —
        // this is what makes an array<int> parameter from a metadata assembly bind as array<int>
        // rather than array<Int32>.
        if (TryMapPrimitiveByFullName(dotNetType.FullName, out var primitive))
            return primitive;

        if (_typeMap.TryGetValue(dotNetType, out var mapped))
            return mapped;

        // All .NET enums are int-backed — map to int
        if (dotNetType.IsEnum)
            return TypeSymbol.Int;

        // Handle nullable types
        if (dotNetType.IsGenericType && dotNetType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return MapToProLangType(dotNetType.GetGenericArguments()[0]);
        }

        // Handle arrays
        if (dotNetType.IsArray)
        {
            var elementType = MapToProLangType(dotNetType.GetElementType()!);
            return TypeSymbol.Array.WithArgs(elementType);
        }

        // Handle generic List<T>
        if (dotNetType.IsGenericType && dotNetType.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = MapToProLangType(dotNetType.GetGenericArguments()[0]);
            return TypeSymbol.Array.WithArgs(elementType);
        }

        // Handle generic Dictionary<TKey, TValue>
        if (dotNetType.IsGenericType && dotNetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keyType = MapToProLangType(dotNetType.GetGenericArguments()[0]);
            var valueType = MapToProLangType(dotNetType.GetGenericArguments()[1]);
            return TypeSymbol.Map.WithArgs(keyType, valueType);
        }

        // System.Object is exactly what ProLang means by `any`, so it must map to the symbol the
        // rest of the compiler tests for by reference. Letting it become a DotNetTypeSymbol broke
        // boxing: EmitConversionExpression boxes when the target is TypeSymbol.Any, and a
        // distinct symbol for the same type silently failed that check.
        if (dotNetType == typeof(object) || dotNetType.FullName == "System.Object")
        {
            return TypeSymbol.Any;
        }

        // Anything else keeps its .NET identity rather than collapsing to 'any'.
        //
        // Returning TypeSymbol.Any here was what made instance calls impossible to bind: the
        // binder saw only "object" and had nothing to resolve a member name against. A
        // DotNetTypeSymbol is still System.Object-compatible everywhere 'any' is accepted, so
        // this widens what can be expressed without narrowing what already worked.
        return new DotNetTypeSymbol(dotNetType);
    }

    /// <summary>
    /// Maps a primitive .NET type to its ProLang symbol by full name.
    /// </summary>
    /// <remarks>
    /// The reference-keyed <see cref="_typeMap"/> only matches the compiler's own
    /// <c>typeof(...)</c> instances. A type read from a metadata assembly is a different
    /// <see cref="Type"/> object with the same full name, so primitives are matched by name here
    /// before the dictionary is consulted.
    /// </remarks>
    private static bool TryMapPrimitiveByFullName(string? fullName, out TypeSymbol symbol)
    {
        switch (fullName)
        {
            case "System.Void": symbol = TypeSymbol.Void; return true;
            case "System.Boolean": symbol = TypeSymbol.Bool; return true;
            case "System.Byte":
            case "System.SByte":
            case "System.Int16":
            case "System.UInt16":
            case "System.Int32":
            case "System.UInt32":
            case "System.Int64":
            case "System.UInt64":
                symbol = TypeSymbol.Int; return true;
            case "System.Single": symbol = TypeSymbol.Float32; return true;
            case "System.Double":
            case "System.Decimal":
                symbol = TypeSymbol.Float64; return true;
            case "System.String":
            case "System.Char":
                symbol = TypeSymbol.String; return true;
            default:
                symbol = TypeSymbol.Any; return false;
        }
    }

    /// <summary>
    /// Creates a type symbol with a custom name for .NET types.
    /// Useful for representing specific .NET types in diagnostics.
    /// </summary>
    public static TypeSymbol CreateDotNetTypeSymbol(Type dotNetType)
    {
        return new TypeSymbol($"dotnet:{dotNetType.FullName}");
    }

    /// <summary>
    /// Converts a ProLang runtime value to the expected .NET type.
    /// </summary>
    /// <remarks>
    /// Only ever called with runtime types the compiler itself owns (primitives, List&lt;object&gt;,
    /// Dictionary&lt;object,object&gt;) — never with a metadata-only interop type, which cannot hold
    /// a value inside the compiler process. The Color conversion is gone for that reason: it
    /// reflection-invoked <c>Color.FromArgb</c> on a loaded assembly, which metadata-only
    /// discovery no longer permits. The emitter handles colours by emitting a call instead.
    /// </remarks>
    public static object? ConvertToDotNet(object? value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType == typeof(object))
            return value;

        if (targetType.IsInstanceOfType(value))
            return value;

        // Handle numeric conversions
        if (IsNumericType(targetType) && (value is int || value is float || value is double))
        {
            return Convert.ChangeType(value, targetType);
        }

        // Handle string conversions
        if (targetType == typeof(string))
            return value.ToString();

        // Handle bool
        if (targetType == typeof(bool) && value is bool)
            return value;

        // Handle char from string
        if (targetType == typeof(char) && value is string str && str.Length == 1)
            return str[0];

        // Handle List<object> to typed List<T>
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (value is List<object> list)
            {
                var elementType = targetType.GetGenericArguments()[0];
                var typedList = (System.Collections.IList)Activator.CreateInstance(targetType)!;
                foreach (var item in list)
                {
                    typedList.Add(ConvertToDotNet(item, elementType));
                }
                return typedList;
            }
        }

        // Handle Dictionary<object,object> to typed Dictionary<TKey,TValue>
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            if (value is Dictionary<object, object> dict)
            {
                var keyType = targetType.GetGenericArguments()[0];
                var valueType = targetType.GetGenericArguments()[1];
                var typedDict = (System.Collections.IDictionary)Activator.CreateInstance(targetType)!;
                foreach (var kvp in dict)
                {
                    typedDict.Add(ConvertToDotNet(kvp.Key, keyType), ConvertToDotNet(kvp.Value, valueType));
                }
                return typedDict;
            }
        }

        return value;
    }

    /// <summary>
    /// Converts a .NET return value to a ProLang runtime value.
    /// </summary>
    /// <remarks>
    /// Like <see cref="ConvertToDotNet"/>, this only ever sees values the compiler itself owns —
    /// primitives and collections. The Color branch is gone: it reflection-invoked
    /// <c>ToArgb</c> on a loaded assembly, which metadata-only discovery no longer permits.
    /// </remarks>
    public static object? ConvertFromDotNet(object? value)
    {
        if (value == null)
            return null;

        // Convert numeric types to int
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            return Convert.ToInt32(value);
        }

        if (value is float f)
            return f;

        if (value is double d)
            return d;

        if (value is decimal dec)
            return (double)dec;

        // Convert char to string
        if (value is char c)
        {
            return c.ToString();
        }

        // Convert typed arrays to List<object>
        if (value is Array arr)
        {
            var list = new List<object>();
            foreach (var item in arr)
            {
                list.Add(ConvertFromDotNet(item));
            }
            return list;
        }

        // Convert typed List<T> to List<object>
        var type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = new List<object>();
            foreach (var item in (System.Collections.IEnumerable)value)
            {
                list.Add(ConvertFromDotNet(item));
            }
            return list;
        }

        // Convert typed Dictionary<TKey,TValue> to Dictionary<object,object>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dict = new Dictionary<object, object>();
            foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)value)
            {
                dict[ConvertFromDotNet(entry.Key)!] = ConvertFromDotNet(entry.Value);
            }
            return dict;
        }

        return value;
    }

    /// <summary>
    /// Gets the default value for a ProLang type symbol.
    /// </summary>
    public static object? GetDefaultValue(TypeSymbol type)
    {
        if (type == TypeSymbol.Int) return 0;
        if (type == TypeSymbol.Bool) return false;
        if (type == TypeSymbol.String) return "";
        if (type == TypeSymbol.Float32) return 0.0f;
        if (type == TypeSymbol.Float64 || type == TypeSymbol.Float) return 0.0d;
        return null;
    }

    /// <summary>
    /// Checks if a .NET type is numeric.
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    /// <summary>
    /// Resolves method arguments, handling parameter type conversions.
    /// </summary>
    public static object?[] PrepareArguments(object?[] proLangArgs, ParameterInfo[] parameters)
    {
        var result = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            if (i < proLangArgs.Length)
            {
                result[i] = ConvertToDotNet(proLangArgs[i], parameters[i].ParameterType);
            }
            else if (parameters[i].HasDefaultValue)
            {
                result[i] = parameters[i].DefaultValue;
            }
            else
            {
                result[i] = GetDefaultValue(MapToProLangType(parameters[i].ParameterType));
            }
        }

        return result;
    }
}
