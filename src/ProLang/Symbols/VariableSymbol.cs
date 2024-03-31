﻿namespace ProLang.Symbols;

public class VariableSymbol : Symbol
{
    internal VariableSymbol(string name, bool isReadOnly, TypeSymbol type): base(name)
    {
        IsReadOnly = isReadOnly;
        Type = type;
    }

    public override SymbolKind Kind => SymbolKind.Variable;
    
    public bool IsReadOnly { get; }
    
    public TypeSymbol Type { get; }
}