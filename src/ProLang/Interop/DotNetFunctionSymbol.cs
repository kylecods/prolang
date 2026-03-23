using System.Collections.Immutable;
using System.Reflection;
using ProLang.Symbols;

namespace ProLang.Interop;

/// <summary>
/// Represents a .NET method as a ProLang function symbol.
/// Can wrap static methods, instance methods, or constructors.
/// </summary>
public sealed class DotNetFunctionSymbol : FunctionSymbol
{
    public DotNetFunctionSymbol(
        string name,
        ImmutableArray<ParameterSymbol> parameters,
        TypeSymbol type,
        MethodInfo? methodInfo,
        ConstructorInfo? constructorInfo,
        Type declaringType,
        bool isStatic,
        TypeSymbol? ownerTypeSymbol = null) 
        : base(name, parameters, type, null)
    {
        MethodInfo = methodInfo;
        ConstructorInfo = constructorInfo;
        DeclaringType = declaringType;
        IsStatic = isStatic;
        OwnerTypeSymbol = ownerTypeSymbol;
    }

    /// <summary>
    /// Creates a DotNetFunctionSymbol from a static .NET method.
    /// </summary>
    public static DotNetFunctionSymbol FromStaticMethod(MethodInfo method)
    {
        var parameters = CreateParameters(method.GetParameters());
        var returnType = DotNetTypeMapper.MapToProLangType(method.ReturnType);

        return new DotNetFunctionSymbol(
            method.Name,
            parameters,
            returnType,
            method,
            null,
            method.DeclaringType!,
            isStatic: true);
    }

    /// <summary>
    /// Creates a DotNetFunctionSymbol from a .NET constructor.
    /// </summary>
    public static DotNetFunctionSymbol FromConstructor(ConstructorInfo constructor)
    {
        var parameters = CreateParameters(constructor.GetParameters());
        var returnType = DotNetTypeMapper.MapToProLangType(constructor.DeclaringType!);

        return new DotNetFunctionSymbol(
            $"new {constructor.DeclaringType!.Name}",
            parameters,
            returnType,
            null,
            constructor,
            constructor.DeclaringType!,
            isStatic: false);
    }

    /// <summary>
    /// Creates a DotNetFunctionSymbol from a static property getter.
    /// </summary>
    public static DotNetFunctionSymbol FromStaticProperty(PropertyInfo property)
    {
        var returnType = DotNetTypeMapper.MapToProLangType(property.PropertyType);

        return new DotNetFunctionSymbol(
            property.Name,
            ImmutableArray<ParameterSymbol>.Empty,
            returnType,
            property.GetMethod!,
            null,
            property.DeclaringType!,
            isStatic: true);
    }

    /// <summary>
    /// Creates a DotNetFunctionSymbol from a static field.
    /// </summary>
    public static DotNetFunctionSymbol FromStaticField(FieldInfo field)
    {
        var returnType = DotNetTypeMapper.MapToProLangType(field.FieldType);

        // We'll treat static fields as zero-argument functions that return the field value
        return new DotNetFunctionSymbol(
            field.Name,
            ImmutableArray<ParameterSymbol>.Empty,
            returnType,
            null,
            null,
            field.DeclaringType!,
            isStatic: true);
    }

    private static ImmutableArray<ParameterSymbol> CreateParameters(ParameterInfo[] paramInfos)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterSymbol>();

        for (int i = 0; i < paramInfos.Length; i++)
        {
            var p = paramInfos[i];
            var typeSymbol = DotNetTypeMapper.MapToProLangType(p.ParameterType);
            builder.Add(new ParameterSymbol(p.Name ?? $"arg{i}", typeSymbol, i));
        }

        return builder.ToImmutable();
    }

    public MethodInfo? MethodInfo { get; }
    public ConstructorInfo? ConstructorInfo { get; }
    public Type DeclaringType { get; }
    public bool IsStatic { get; }
    public TypeSymbol? OwnerTypeSymbol { get; }

    /// <summary>
    /// Invokes this function at runtime.
    /// </summary>
    public object? Invoke(object?[] arguments, object? instance = null)
    {
        if (ConstructorInfo != null)
        {
            var args = DotNetTypeMapper.PrepareArguments(arguments, ConstructorInfo.GetParameters());
            var result = ConstructorInfo.Invoke(args);
            return DotNetTypeMapper.ConvertFromDotNet(result);
        }

        if (MethodInfo != null)
        {
            var args = DotNetTypeMapper.PrepareArguments(arguments, MethodInfo.GetParameters());
            object? target = IsStatic ? null : instance;
            var result = MethodInfo.Invoke(target, args);
            return DotNetTypeMapper.ConvertFromDotNet(result);
        }

        // Static field access
        if (IsStatic && ConstructorInfo == null && MethodInfo == null)
        {
            var field = DeclaringType.GetField(Name);
            if (field != null)
            {
                return DotNetTypeMapper.ConvertFromDotNet(field.GetValue(null));
            }

            var property = DeclaringType.GetProperty(Name);
            if (property != null)
            {
                return DotNetTypeMapper.ConvertFromDotNet(property.GetValue(null));
            }
        }

        throw new InvalidOperationException($"Cannot invoke .NET member '{Name}'");
    }

    public override string ToString()
    {
        var paramList = string.Join(", ", Parameters.Select(p => $"{p.Name}: {p.Type}"));
        return $"dotnet {DeclaringType.Name}.{Name}({paramList}): {Type}";
    }
}
