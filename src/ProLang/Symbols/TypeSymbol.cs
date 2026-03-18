using System.Collections.Immutable;

namespace ProLang.Symbols;

public sealed class TypeSymbol : Symbol
{
    public static readonly TypeSymbol Error = new("?");
    public static readonly TypeSymbol Bool = new("bool");
    public static readonly TypeSymbol Int = new("int");
    public static readonly TypeSymbol String = new("string");
    public static readonly TypeSymbol Void = new("void");
    public static readonly TypeSymbol Any = new("any");
    public static readonly TypeSymbol Array = new("array");
    public static readonly TypeSymbol Map = new("map");

    public TypeSymbol(string name, ImmutableArray<TypeSymbol> typeArguments = default) : base(name)
    {
        TypeArguments = typeArguments.IsDefault ? ImmutableArray<TypeSymbol>.Empty : typeArguments;
    }

    public ImmutableArray<TypeSymbol> TypeArguments { get; }

    public override SymbolKind Kind => SymbolKind.Type;

    public TypeSymbol WithArgs(params TypeSymbol[] args)
    {
        return new TypeSymbol(Name, args.ToImmutableArray());
    }

    public override string ToString()
    {
        if (TypeArguments.Length == 0)
            return Name;

        return $"{Name}<{string.Join(", ", TypeArguments)}>";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not TypeSymbol other) return false;

        if (Name != other.Name) return false;
        if (TypeArguments.Length != other.TypeArguments.Length) return false;

        for (int i = 0; i < TypeArguments.Length; i++)
        {
            if (TypeArguments[i] != other.TypeArguments[i]) return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hashCode = Name.GetHashCode();
        foreach (var arg in TypeArguments)
        {
            hashCode = HashCode.Combine(hashCode, arg.Name.GetHashCode());
        }
        return hashCode;
    }

    public static bool operator ==(TypeSymbol? left, TypeSymbol? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(TypeSymbol? left, TypeSymbol? right)
    {
        return !(left == right);
    }
}