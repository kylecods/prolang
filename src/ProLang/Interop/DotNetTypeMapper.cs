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
        RegisterPrimitiveMapping(typeof(float), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(double), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(decimal), TypeSymbol.Int);
        RegisterPrimitiveMapping(typeof(string), TypeSymbol.String);
        RegisterPrimitiveMapping(typeof(char), TypeSymbol.String);
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
        if (_typeMap.TryGetValue(dotNetType, out var mapped))
            return mapped;

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

        // Everything else maps to 'any'
        return TypeSymbol.Any;
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
    public static object? ConvertToDotNet(object? value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType == typeof(object))
            return value;

        if (targetType.IsInstanceOfType(value))
            return value;

        // Handle numeric conversions
        if (IsNumericType(targetType) && value is int)
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
    public static object? ConvertFromDotNet(object? value)
    {
        if (value == null)
            return null;

        // Convert numeric types to int
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            return Convert.ToInt32(value);
        }

        if (value is float or double or decimal)
        {
            return Convert.ToInt32(value);
        }

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
