namespace ProLang.Symbols;

public enum SymbolKind
{
    GlobalVariable,
    LocalVariable,
    Type,
    Parameter,
    Function,

    // Structs and enums both used to report Type, and their members had no symbol at all. The
    // compiler never needed the distinction; an editor does — it is what decides the icon beside a
    // completion, the colour of a token, and whether a document outline nests a name under another.
    Struct,
    Enum,
    Field,
    EnumMember,
}