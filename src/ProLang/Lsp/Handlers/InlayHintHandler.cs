using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// The type of an inferred <c>let</c>, shown where the annotation would have been.
/// </summary>
/// <remarks>
/// <para>
/// ProLang's type inference is worth having and it costs the reader something: <c>let count = 0</c>
/// says less than <c>let count: int = 0</c>. A hint gives back what the annotation would have said
/// without anyone having to write it.
/// </para>
/// <para>
/// Only where a type clause was omitted. Repeating one that is already written would be noise.
/// </para>
/// </remarks>
internal static class InlayHintHandler
{
    public static List<InlayHint> Hints(Analysis analysis, Protocol.Range range)
    {
        var hints = new List<InlayHint>();
        var model = analysis.Model;

        if (model == null)
        {
            return hints;
        }

        var text = analysis.Text;
        var from = LspConversions.ToOffset(text, range.Start);
        var to = LspConversions.ToOffset(text, range.End);

        // Lexed once, not once per hint. A file's worth of hints times a file's worth of tokens is
        // the kind of quadratic that only shows up on the large modules where it matters.
        var annotated = DeclarationsWithAWrittenType(text);

        foreach (var occurrence in model.OccurrencesIn(text.FileName))
        {
            if (!occurrence.IsDefinition || occurrence.Span.Start < from || occurrence.Span.End > to)
                continue;

            if (occurrence.Symbol is not VariableSymbol variable || variable.Kind == SymbolKind.Parameter)
                continue;

            if (annotated.Contains(occurrence.Span.Start))
                continue;

            var end = occurrence.Location;

            hints.Add(new InlayHint
            {
                Position = new Protocol.Position { Line = end.EndLine, Character = end.EndCharacter },
                Label = ": " + variable.Type,
                Kind = 1,
            });
        }

        return hints;
    }

    /// <summary>
    /// The start offsets of every name that is followed by a type clause.
    /// </summary>
    /// <remarks>
    /// Decided by the token after the name: a type clause is written as a colon, and nothing else
    /// can follow a declared name. More robust than looking the declaration up in the tree, where
    /// a <c>for</c> loop's variable is not a variable statement at all — and it works on a file
    /// that does not parse.
    /// </remarks>
    private static HashSet<int> DeclarationsWithAWrittenType(ProLang.Text.SourceText text)
    {
        var annotated = new HashSet<int>();
        var previousStart = -1;

        foreach (var token in SyntaxTree.ParseTokens(text))
        {
            if (token.Kind == SyntaxKind.WhitespaceToken)
                continue;

            if (token.Kind == SyntaxKind.ColonToken && previousStart >= 0)
            {
                annotated.Add(previousStart);
            }

            previousStart = token.Kind == SyntaxKind.IdentifierToken ? token.Position : -1;
        }

        return annotated;
    }
}
