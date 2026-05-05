namespace ProLang.Symbols;

public sealed class TypeParameterSymbol : TypeSymbol
{
    public TypeParameterSymbol(string name, int index)
        : base(name)
    {
        Index = index;
    }

    public int Index { get; }

    public override string ToString() => Name;
}
