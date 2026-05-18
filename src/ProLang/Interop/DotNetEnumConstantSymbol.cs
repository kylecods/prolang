using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Interop;

/// <summary>
/// Represents a .NET enum member as a zero-argument function returning a constant int.
/// Avoids reflection — the value is stored directly.
/// </summary>
public sealed class DotNetEnumConstantSymbol : FunctionSymbol
{
    public DotNetEnumConstantSymbol(string qualifiedName, int value)
        : base(qualifiedName, ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Int)
    {
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => $"enum const {Name} = {Value}";
}
