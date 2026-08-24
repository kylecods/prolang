using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Symbols;
using ProLang.Text;

namespace ProLang.Lsp;

/// <summary>
/// Translation between the compiler's vocabulary and the protocol's.
/// </summary>
internal static class LspConversions
{
    /// <summary>
    /// A compiler location as a protocol range.
    /// </summary>
    /// <remarks>
    /// Both count lines and characters from zero, and both count characters in UTF-16 code units —
    /// the protocol's default encoding, and what a .NET string is made of — so this is arithmetic
    /// rather than conversion. The guard is for locations with no text at all: several diagnostics
    /// are reported with a default location, and asking one for its line throws.
    /// </remarks>
    public static Protocol.Range ToRange(TextLocation location)
    {
        if (location.Text == null)
        {
            return new Protocol.Range();
        }

        return new Protocol.Range
        {
            Start = new Protocol.Position { Line = location.StartLine, Character = location.StartCharacter },
            End = new Protocol.Position { Line = location.EndLine, Character = location.EndCharacter },
        };
    }

    public static Protocol.Range ToRange(SourceText text, TextSpan span) =>
        ToRange(new TextLocation(text, span));

    public static Protocol.Location ToLocation(TextLocation location) => new()
    {
        Uri = DocumentUri.FromPath(location.FileName),
        Range = ToRange(location),
    };

    public static int ToOffset(SourceText text, Protocol.Position position) =>
        text.GetPosition(position.Line, position.Character);

    /// <summary>The completion icon for a symbol.</summary>
    public static int ToCompletionKind(Symbol symbol) => symbol.Kind switch
    {
        SymbolKind.Function => Protocol.CompletionItemKind.Function,
        SymbolKind.Struct => Protocol.CompletionItemKind.Struct,
        SymbolKind.Enum => Protocol.CompletionItemKind.Enum,
        SymbolKind.EnumMember => Protocol.CompletionItemKind.EnumMember,
        SymbolKind.Field => Protocol.CompletionItemKind.Field,
        SymbolKind.Parameter => Protocol.CompletionItemKind.Variable,
        SymbolKind.Type => Protocol.CompletionItemKind.TypeParameter,
        _ => Protocol.CompletionItemKind.Variable,
    };

    public static int ToSymbolKind(Symbol symbol) => symbol.Kind switch
    {
        SymbolKind.Function => SymbolKinds.Function,
        SymbolKind.Struct => SymbolKinds.Struct,
        SymbolKind.Enum => SymbolKinds.Enum,
        SymbolKind.EnumMember => SymbolKinds.EnumMember,
        SymbolKind.Field => SymbolKinds.Field,
        _ => SymbolKinds.Variable,
    };

    /// <summary>
    /// A symbol rendered the way it is written in source, for the top line of a hover.
    /// </summary>
    /// <remarks>
    /// Through <see cref="Symbol.WriteTo"/> rather than <c>ToString()</c>, because
    /// <see cref="TypeSymbol"/> overrides <c>ToString</c> to return its bare name — so a struct
    /// would render as <c>Point</c> rather than <c>struct Point</c>. <see cref="SymbolPrinter"/>
    /// colours its output only when writing to the console, so writing it to a string gives plain
    /// text that can go straight into a Markdown code fence.
    /// </remarks>
    public static string Signature(Symbol symbol)
    {
        using var writer = new StringWriter();

        symbol.WriteTo(writer);

        return writer.ToString();
    }

    /// <summary>A symbol as Markdown: its signature in a ProLang fence, then its documentation.</summary>
    public static string ToMarkdown(Symbol symbol, string? extra = null)
    {
        var markdown = $"```prolang\n{Signature(symbol)}\n```";

        if (!string.IsNullOrWhiteSpace(symbol.Documentation))
        {
            markdown += "\n\n" + symbol.Documentation;
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            markdown += "\n\n" + extra;
        }

        return markdown;
    }
}
