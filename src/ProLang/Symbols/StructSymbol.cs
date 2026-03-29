using System.Collections.Immutable;

namespace ProLang.Symbols;

public sealed class StructSymbol : TypeSymbol
{
    public StructSymbol(string name, ImmutableArray<StructField> fields) : base(name)
    {
        Fields = fields;
    }

    public ImmutableArray<StructField> Fields { get; }

    public override SymbolKind Kind => SymbolKind.Type;
}