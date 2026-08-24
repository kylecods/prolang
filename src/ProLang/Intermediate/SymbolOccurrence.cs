using ProLang.Symbols;
using ProLang.Text;

namespace ProLang.Intermediate;

/// <summary>What a mention of a symbol is doing.</summary>
internal enum OccurrenceKind
{
    /// <summary>The place the symbol is introduced — where "go to definition" should land.</summary>
    Definition,

    /// <summary>A use of the symbol in an expression.</summary>
    Reference,

    /// <summary>A use of the symbol as a type: a type clause, a cast target, a struct creation.</summary>
    TypeReference,
}

/// <summary>
/// One mention of one symbol at one place in the source.
/// </summary>
/// <remarks>
/// <para>
/// This is the compiler's answer to "what is under the cursor", and it exists because the bound
/// tree cannot answer it. <see cref="BoundNode"/> carries no span and no syntax back-reference,
/// and — the part that settles it — types never become bound nodes at all: binding a type clause
/// yields a bare <see cref="TypeSymbol"/>, so no amount of annotating the bound tree would ever
/// let anyone navigate from the <c>Point</c> in <c>let p: Point</c> to the struct.
/// </para>
/// <para>
/// The binder knows both halves at the moment it resolves a name, so it records them as it goes.
/// Every editor navigation feature — hover, go to definition, find references, rename, highlight,
/// semantic colouring — is a query over this one list.
/// </para>
/// </remarks>
internal readonly record struct SymbolOccurrence(TextLocation Location, Symbol Symbol, OccurrenceKind Kind)
{
    public string FileName => Location.FileName;

    public TextSpan Span => Location.Span;

    public bool IsDefinition => Kind == OccurrenceKind.Definition;
}
