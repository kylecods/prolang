using ProLang.Intermediate;
using ProLang.Lsp.Workspace;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// Colouring driven by what each name actually resolved to.
/// </summary>
/// <remarks>
/// A TextMate grammar can only guess from shape: to it, every identifier looks the same, so a
/// struct, a parameter, an enum member and a builtin are all coloured alike. Here the binder has
/// already decided what each one is, so the colour follows the meaning — and a name that resolves
/// to nothing simply gets no token, which is a quiet, useful signal in itself.
/// </remarks>
internal static class SemanticTokensHandler
{
    /// <summary>The token types, in the order the client is told about them at initialisation.</summary>
    public static readonly string[] TokenTypes =
    [
        "function", "variable", "parameter", "property", "enumMember", "struct", "enum", "type",
        "keyword", "comment", "string", "number", "operator",
    ];

    public static readonly string[] TokenModifiers = ["declaration", "defaultLibrary"];

    private const int Function = 0;
    private const int Variable = 1;
    private const int Parameter = 2;
    private const int Property = 3;
    private const int EnumMember = 4;
    private const int Struct = 5;
    private const int Enum = 6;
    private const int Type = 7;

    private const int DeclarationModifier = 1 << 0;
    private const int DefaultLibraryModifier = 1 << 1;

    /// <summary>
    /// The tokens of one file, in the protocol's delta encoding.
    /// </summary>
    /// <remarks>
    /// Five integers per token, each position relative to the token before it: line delta,
    /// character delta (from the previous token's start, or from the line start on a new line),
    /// length, type, modifiers. Sorted by position, which the occurrence list is not — it is
    /// ordered innermost-span-first for lookup.
    /// </remarks>
    public static List<int> Tokens(Analysis analysis)
    {
        var data = new List<int>();
        var model = analysis.Model;

        if (model == null)
        {
            return data;
        }

        var occurrences = model.OccurrencesIn(analysis.Text.FileName)
            .OrderBy(o => o.Span.Start)
            .ToList();

        var lastLine = 0;
        var lastStart = 0;

        foreach (var occurrence in occurrences)
        {
            var location = occurrence.Location;
            var line = location.StartLine;
            var start = location.StartCharacter;

            // A token that spans lines cannot be encoded, and no identifier ever does.
            if (location.EndLine != line)
            {
                continue;
            }

            data.Add(line - lastLine);
            data.Add(line == lastLine ? start - lastStart : start);
            data.Add(occurrence.Span.Length);
            data.Add(TypeOf(occurrence.Symbol));
            data.Add(ModifiersOf(occurrence, model));

            lastLine = line;
            lastStart = start;
        }

        return data;
    }

    private static int TypeOf(Symbol symbol) => symbol.Kind switch
    {
        SymbolKind.Function => Function,
        SymbolKind.Parameter => Parameter,
        SymbolKind.Field => Property,
        SymbolKind.EnumMember => EnumMember,
        SymbolKind.Struct => Struct,
        SymbolKind.Enum => Enum,
        SymbolKind.Type => Type,
        _ => Variable,
    };

    private static int ModifiersOf(SymbolOccurrence occurrence, Compiler.SemanticModel model)
    {
        var modifiers = 0;

        if (occurrence.IsDefinition)
        {
            modifiers |= DeclarationModifier;
        }

        // No definition anywhere in source means it came from the compiler: a builtin. Marking it
        // is what lets an editor show `print` differently from a function someone wrote.
        if (model.FindDefinition(occurrence.Symbol) == null)
        {
            modifiers |= DefaultLibraryModifier;
        }

        return modifiers;
    }
}

/// <summary>
/// Foldable regions, from the shape of the source.
/// </summary>
internal static class FoldingHandler
{
    public static List<Protocol.FoldingRange> Ranges(Analysis analysis)
    {
        var ranges = new List<Protocol.FoldingRange>();
        var text = analysis.Text;

        foreach (var declaration in analysis.SyntaxTree.Root.Declarations)
        {
            if (declaration is not (FunctionDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax))
                continue;

            var startLine = text.GetLineIndex(declaration.Span.Start);
            var endLine = text.GetLineIndex(Math.Max(declaration.Span.Start, declaration.Span.End - 1));

            if (endLine > startLine)
            {
                ranges.Add(new Protocol.FoldingRange { StartLine = startLine, EndLine = endLine });
            }
        }

        ranges.AddRange(CommentAndImportRuns(analysis));

        return ranges;
    }

    /// <summary>
    /// Block comments, and runs of imports.
    /// </summary>
    /// <remarks>
    /// Both are worth folding for the same reason: they sit at the top of a file, they are long in
    /// this repository's style, and neither is what someone opening the file came to read.
    /// </remarks>
    private static IEnumerable<Protocol.FoldingRange> CommentAndImportRuns(Analysis analysis)
    {
        var text = analysis.Text;

        foreach (var token in SyntaxTree.ParseTokens(text))
        {
            if (token.Kind != SyntaxKind.WhitespaceToken || token.Text?.Contains("/*") != true)
                continue;

            var startLine = text.GetLineIndex(token.Position);
            var endLine = text.GetLineIndex(Math.Min(text.Length - 1, token.Position + token.Text.Length - 1));

            if (endLine > startLine)
            {
                yield return new Protocol.FoldingRange { StartLine = startLine, EndLine = endLine, Kind = "comment" };
            }
        }

        var imports = analysis.SyntaxTree.Root.Declarations
            .OfType<ImportDeclarationSyntax>()
            .Select(i => text.GetLineIndex(i.Span.Start))
            .OrderBy(line => line)
            .ToList();

        if (imports.Count > 1 && imports[^1] - imports[0] == imports.Count - 1)
        {
            yield return new Protocol.FoldingRange
            {
                StartLine = imports[0],
                EndLine = imports[^1],
                Kind = "imports",
            };
        }
    }
}
