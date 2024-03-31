﻿namespace ProLang.Symbols;

public sealed class TypeSymbol : Symbol
{
    public static readonly TypeSymbol Error = new("?");
    public static readonly TypeSymbol Bool = new("bool");
    public static readonly TypeSymbol Int = new("int");
    public static readonly TypeSymbol String = new("string");
    private TypeSymbol(string name) : base(name)
    {
    }

    public override SymbolKind Kind => SymbolKind.Type;
}