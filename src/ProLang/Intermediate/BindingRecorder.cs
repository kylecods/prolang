using System.Collections.Immutable;
using ProLang.Symbols;
using ProLang.Text;

namespace ProLang.Intermediate;

/// <summary>
/// Collects what the binder resolved, and where, as it resolves it.
/// </summary>
/// <remarks>
/// <para>
/// The binder holds both halves of every question an editor asks — a syntax node with a span, and
/// the symbol that node turned out to mean — for exactly as long as it takes to bind, and then
/// drops both. This keeps them.
/// </para>
/// <para>
/// It is attached only when something asks for it, so an ordinary compile pays one null check per
/// bind site and allocates nothing.
/// </para>
/// </remarks>
internal sealed class BindingRecorder
{
    private readonly List<SymbolOccurrence> _occurrences = new();
    private readonly HashSet<(string File, int Start, int Length, Symbol Symbol)> _seen = new();
    private readonly Dictionary<(string File, int Start, int Length), TypeSymbol> _expressionTypes = new();
    private readonly Dictionary<(EnumSymbol Declaring, string Member), EnumMemberSymbol> _enumMembers = new();

    public IReadOnlyList<SymbolOccurrence> Occurrences => _occurrences;

    /// <summary>Records that <paramref name="symbol"/> is named at <paramref name="location"/>.</summary>
    public void RecordSymbol(TextLocation location, Symbol? symbol, OccurrenceKind kind)
    {
        if (symbol == null || !IsRealLocation(location))
        {
            return;
        }

        symbol = Canonicalise(symbol);

        var key = (location.FileName, location.Span.Start, location.Span.Length, symbol);

        if (!_seen.Add(key))
        {
            return;
        }

        _occurrences.Add(new SymbolOccurrence(location, symbol, kind));
    }

    /// <summary>Records the type an expression was bound to.</summary>
    /// <remarks>
    /// Called from the one place every expression passes through, which is what makes it worth
    /// having: it types spans the occurrence list knows nothing about — the result of a call, an
    /// index, a parenthesised expression — and that is what member completion needs to answer
    /// <c>getBox(i).</c> or <c>arr[0].</c> rather than only <c>name.</c>.
    /// </remarks>
    public void RecordExpressionType(TextLocation location, TypeSymbol? type)
    {
        if (type == null || type == TypeSymbol.Error || !IsRealLocation(location))
        {
            return;
        }

        _expressionTypes[(location.FileName, location.Span.Start, location.Span.Length)] = type;
    }

    public bool TryGetExpressionType(string fileName, TextSpan span, out TypeSymbol type) =>
        _expressionTypes.TryGetValue((fileName, span.Start, span.Length), out type!);

    /// <summary>The symbol for one enum member, the same instance every time it is asked for.</summary>
    /// <remarks>
    /// Identity is the point. Two mentions of <c>Colour.Green</c> must be the same symbol or they
    /// do not group together as references to each other.
    /// </remarks>
    public EnumMemberSymbol GetEnumMember(EnumSymbol declaring, EnumMember member)
    {
        var key = (declaring, member.Name);

        if (!_enumMembers.TryGetValue(key, out var symbol))
        {
            symbol = new EnumMemberSymbol(declaring, member);
            _enumMembers[key] = symbol;
        }

        return symbol;
    }

    public ImmutableArray<SymbolOccurrence> ToImmutable() => _occurrences.ToImmutableArray();

    /// <summary>
    /// Maps an instantiated generic back to the template it came from.
    /// </summary>
    /// <remarks>
    /// A generic is bound once per set of type arguments, so <c>DynArray&lt;int&gt;</c> and
    /// <c>DynArray&lt;string&gt;</c> produce two symbols over the same source span. Recording them
    /// separately would make "find references" on the template find nothing, and would return the
    /// same span twice from two different symbols. The template is the thing that exists in the
    /// source, so the template is what is recorded.
    /// </remarks>
    private static Symbol Canonicalise(Symbol symbol) => symbol switch
    {
        FunctionSymbol { OriginalGeneric: not null } f => f.OriginalGeneric,
        StructSymbol { OriginalGeneric: not null } s => s.OriginalGeneric,
        _ => symbol,
    };

    /// <summary>
    /// Whether a location points at real text.
    /// </summary>
    /// <remarks>
    /// Two things it rejects. A <c>default</c> <see cref="TextLocation"/>, whose <c>Text</c> is
    /// null — several diagnostics are reported with one, and reading its line would throw. And a
    /// zero-length span, which is what the parser inserts to recover from a token that was never
    /// typed: there is nothing there to hover, click or rename.
    /// </remarks>
    private static bool IsRealLocation(TextLocation location) =>
        location.Text != null && location.Span.Length > 0;
}
