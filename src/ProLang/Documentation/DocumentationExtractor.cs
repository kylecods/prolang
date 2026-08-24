using System.Text;
using ProLang.Text;

namespace ProLang.Documentation;

/// <summary>
/// Recovers the comment written above a declaration, as its documentation.
/// </summary>
/// <remarks>
/// <para>
/// ProLang has no documentation-comment syntax, and it did not need one. The standard library is
/// already thoroughly commented — every module opens with a block explaining what it is for, and
/// most functions have a sentence or a paragraph directly above them — so the documentation an
/// editor should show already exists in the source. It was simply never read.
/// </para>
/// <para>
/// This reads the source text rather than the token stream, because the parser discards comments
/// and there is no trivia model to consult. That is not a workaround: a comment's relationship to
/// a declaration is a matter of where it sits on the page, which is a property of the text.
/// </para>
/// </remarks>
public static class DocumentationExtractor
{
    /// <summary>
    /// The comment block immediately above the declaration starting at <paramref name="declarationStart"/>.
    /// </summary>
    /// <remarks>
    /// "Immediately" is what does the work. The walk upward stops at the first blank line, so a
    /// section banner separated from the code below it by a blank line — which is how every one in
    /// <c>std/ui/</c> is written — is not mistaken for the documentation of whatever follows it.
    /// </remarks>
    public static string? ForDeclaration(SourceText text, int declarationStart)
    {
        if (text.Lines.Length == 0)
        {
            return null;
        }

        var declarationLine = text.GetLineIndex(declarationStart);
        var lines = new List<string>();

        for (var index = declarationLine - 1; index >= 0; index--)
        {
            var line = text.ToString(text.Lines[index].Span).TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                break;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                lines.Insert(0, trimmed);
                continue;
            }

            // A block comment, read from its closing delimiter back to its opening one. Anything
            // between them belongs to the comment whatever it looks like, so no per-line test can
            // be applied while inside one.
            if (trimmed.EndsWith("*/", StringComparison.Ordinal))
            {
                var block = new List<string>();
                var found = false;

                for (; index >= 0; index--)
                {
                    var blockLine = text.ToString(text.Lines[index].Span).TrimEnd();
                    block.Insert(0, blockLine.TrimStart());

                    if (blockLine.TrimStart().StartsWith("/*", StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    break;
                }

                lines.InsertRange(0, block);
                continue;
            }

            break;
        }

        return Clean(lines);
    }

    /// <summary>
    /// The comment a file opens with, as the documentation of the module it is.
    /// </summary>
    /// <remarks>
    /// Only counts when it really is a header: the comment has to start the file and end before
    /// the first line of code, or the first function's own documentation would be shown as the
    /// module's.
    /// </remarks>
    public static string? ForModule(SourceText text)
    {
        var lines = new List<string>();
        var inBlock = false;

        foreach (var textLine in text.Lines)
        {
            var line = text.ToString(textLine.Span).Trim();

            if (inBlock)
            {
                lines.Add(line);

                if (line.EndsWith("*/", StringComparison.Ordinal))
                {
                    inBlock = false;
                }

                continue;
            }

            if (line.Length == 0)
            {
                // A blank line ends the header only once something has been collected; a file may
                // legitimately begin with one.
                if (lines.Count > 0)
                    break;

                continue;
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                inBlock = !line.EndsWith("*/", StringComparison.Ordinal) || line.Length < 4;
                lines.Add(line);
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                lines.Add(line);
                continue;
            }

            break;
        }

        return Clean(lines);
    }

    /// <summary>
    /// Strips comment syntax and decoration, leaving the prose.
    /// </summary>
    /// <remarks>
    /// The rule for a line of nothing but dashes or box-drawing characters is not cosmetic: the
    /// library uses them as rules inside block comments, and rendered as Markdown a run of dashes
    /// under a line of text turns that line into a heading.
    /// </remarks>
    private static string? Clean(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        var cleaned = new List<string>(lines.Count);

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith("/*", StringComparison.Ordinal))
                line = line[2..];

            if (line.EndsWith("*/", StringComparison.Ordinal))
                line = line[..^2];

            line = line.TrimStart();

            if (line.StartsWith("//", StringComparison.Ordinal))
                line = line[2..];
            else if (line.StartsWith('*'))
                line = line[1..];

            line = line.Trim();

            if (IsDecoration(line))
                line = string.Empty;

            cleaned.Add(line);
        }

        while (cleaned.Count > 0 && cleaned[0].Length == 0)
            cleaned.RemoveAt(0);

        while (cleaned.Count > 0 && cleaned[^1].Length == 0)
            cleaned.RemoveAt(cleaned.Count - 1);

        if (cleaned.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();

        for (var i = 0; i < cleaned.Count; i++)
        {
            if (i > 0)
                builder.Append('\n');

            builder.Append(cleaned[i]);
        }

        return builder.ToString();
    }

    private static bool IsDecoration(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        foreach (var c in line)
        {
            if (c is not ('-' or '=' or '~' or '#' or '*' or '_' or ' ' or '─' or '━' or '═' or '·'))
            {
                return false;
            }
        }

        return true;
    }
}
