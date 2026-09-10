using System.Collections.Immutable;

namespace ProLang.Symbols;

public class TypeSymbol : Symbol
{
    public static readonly TypeSymbol Error = new("?");
    public static readonly TypeSymbol Bool = new("bool");
    public static readonly TypeSymbol Int = new("int");//int32
    public static readonly TypeSymbol UInt32 = new("uint32");//uint32
    public static readonly TypeSymbol String = new("string");
    public static readonly TypeSymbol Void = new("void");
    public static readonly TypeSymbol Any = new("any");
    public static readonly TypeSymbol Array = new("array");
    public static readonly TypeSymbol Map = new("map");
    public static readonly TypeSymbol Null = new("null");

    public static readonly TypeSymbol UInt8 = new("uint8");//byte

    public static readonly TypeSymbol Int8 = new("int8");//sbyte

    public static readonly TypeSymbol UInt16 = new("uint16");//ushort

    public static readonly TypeSymbol Char = new("char");// a UTF-16 code unit, System.Char

    public static readonly TypeSymbol Int16 = new("int16");//short

    public static readonly TypeSymbol Int64 = new("int64");//long

    public static readonly TypeSymbol UInt64 = new("uint64");//ulong

    public static readonly TypeSymbol Float   = new("float");   // alias for float64
    public static readonly TypeSymbol Float32 = new("float32"); // System.Single
    public static readonly TypeSymbol Float64 = new("float64"); // System.Double

    /// <summary>
    /// Every type that can be named by a keyword, keyed by the name a program writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One table, so the binder's name resolution and everything that has to enumerate the
    /// primitives — completion, documentation — cannot disagree about what they are. They already
    /// had: the binder's resolution switch had no <c>uint32</c> case, although the symbol has
    /// always existed here and all three backends emit it, so <c>let x: uint32 = 0</c> failed to
    /// bind while every other width worked.
    /// </para>
    /// <para>
    /// <see cref="Error"/> and <see cref="Null"/> are deliberately absent: neither is writable in
    /// source, and offering either in a type position would be offering a program that cannot
    /// compile.
    /// </para>
    /// </remarks>
    public static readonly ImmutableDictionary<string, TypeSymbol> Primitives =
        new Dictionary<string, TypeSymbol>(StringComparer.Ordinal)
        {
            ["any"] = Any,
            ["bool"] = Bool,
            ["int"] = Int,
            ["string"] = String,
            ["void"] = Void,
            ["array"] = Array,
            ["map"] = Map,
            ["uint8"] = UInt8,
            ["int8"] = Int8,
            ["uint16"] = UInt16,
            ["char"] = Char,
            ["int16"] = Int16,
            ["uint32"] = UInt32,
            ["uint64"] = UInt64,
            ["int64"] = Int64,
            ["float"] = Float,
            ["float32"] = Float32,
            ["float64"] = Float64,
        }.ToImmutableDictionary(StringComparer.Ordinal);

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