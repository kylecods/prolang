namespace ProLang.Symbols;

public sealed class StructField
{
    public StructField(string name, TypeSymbol type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
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