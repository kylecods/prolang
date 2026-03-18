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
    private TypeSymbol(string name) : base(name)
    {
    }

    public override SymbolKind Kind => SymbolKind.Type;
}