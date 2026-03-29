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
}