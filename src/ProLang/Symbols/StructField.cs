namespace ProLang.Symbols;

/// <summary>
/// One field of a struct.
/// </summary>
/// <remarks>
/// A <see cref="Symbol"/> so that it can be pointed at: hovering a field, jumping to where it is
/// declared and finding everything that reads it are all queries over symbol occurrences, and a
/// field that is not a symbol cannot appear in one. It was previously a plain class, which is why
/// field access was the one navigation an editor could not follow.
/// </remarks>
public sealed class StructField : Symbol
{
    public StructField(string name, TypeSymbol type) : base(name)
    {
        Type = type;
    }

    public override SymbolKind Kind => SymbolKind.Field;

    public TypeSymbol Type { get; }

    public bool ContainsTypeParameter()
    {
        return ContainsTypeParameterRecursive(Type);
    }

    private static bool ContainsTypeParameterRecursive(TypeSymbol type)
    {
        if (type is TypeParameterSymbol)
            return true;

        if (type.TypeArguments.Length > 0)
        {
            foreach (var arg in type.TypeArguments)
            {
                if (ContainsTypeParameterRecursive(arg))
                    return true;
            }
        }

        return false;
    }
}