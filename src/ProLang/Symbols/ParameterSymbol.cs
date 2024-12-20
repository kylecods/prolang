﻿namespace ProLang.Symbols;

public sealed class ParameterSymbol : LocalVariableSymbol
{
    public ParameterSymbol(string name, TypeSymbol type, int ordinal) : base(name, true, type)
    {
        Ordinal = ordinal;
    }

    public override SymbolKind Kind => SymbolKind.Parameter;
    public int Ordinal { get; }
}