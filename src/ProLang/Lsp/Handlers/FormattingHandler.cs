using System.Text;
using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// Re-indents a document.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately indentation only. A formatter that also decides where spaces go inside a line has
/// to be able to reproduce every construct in the language exactly, and getting one of them wrong
/// silently rewrites working code. Indentation is the part that is mechanical, is what actually
/// drifts, and can be done from the brace depth alone.
/// </para>
/// <para>
/// What this fixes in the one it replaces: that one recomputed the depth by rescanning every
/// preceding line for every line, which is quadratic; counted a line matching both its patterns
/// twice; never dedented a closing brace, so every <c>}</c> came out one level too deep; ignored
/// the editor's tab settings; and returned an end position one line past the end of the document.
/// </para>
/// </remarks>
internal static class FormattingHandler
{
    public static List<TextEdit> Format(Analysis analysis, FormattingOptions options)
    {
        var text = analysis.Text;
        var formatted = Reindent(text, options);

        if (formatted == text.ToString())
        {
            return [];
        }

        var lastLine = text.Lines[^1];

        return
        [
            new TextEdit
            {
                Range = new Protocol.Range
                {
                    Start = new Protocol.Position { Line = 0, Character = 0 },
                    End = new Protocol.Position
                    {
                        Line = text.Lines.Length - 1,
                        Character = lastLine.End - lastLine.Start,
                    },
                },
                NewText = formatted,
            },
        ];
    }

    /// <summary>
    /// The document with every line's leading whitespace replaced by its brace depth.
    /// </summary>
    /// <remarks>
    /// Depth is counted from the token stream, so a brace inside a string or a comment does not
    /// move anything — which is the failure a line-by-line regular expression cannot avoid.
    /// </remarks>
    public static string Reindent(SourceText text, FormattingOptions options)
    {
        var unit = options.InsertSpaces ? new string(' ', Math.Max(1, options.TabSize)) : "\t";
        var newline = DominantLineEnding(text);
        var depthAt = DepthPerLine(text);
        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Lines.Length; i++)
        {
            var line = text.Lines[i];
            var content = text.ToString(line.Span).Trim();

            // A blank line stays blank rather than becoming a line of spaces.
            if (content.Length > 0)
            {
                for (var d = 0; d < depthAt[i]; d++)
                {
                    builder.Append(unit);
                }

                builder.Append(content);
            }

            if (i < text.Lines.Length - 1)
            {
                builder.Append(newline);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The line ending the document already uses.
    /// </summary>
    /// <remarks>
    /// Formatting re-emits every line, so writing <c>\n</c> unconditionally would silently rewrite
    /// the line endings of every file on Windows — turning a one-line indentation fix into a diff
    /// touching the whole file.
    /// </remarks>
    private static string DominantLineEnding(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            var width = line.LengthIncludingLineBreak - line.Length;

            if (width > 0)
            {
                return width == 2 ? "\r\n" : text.ToString(line.Start + line.Length, width);
            }
        }

        return Environment.NewLine;
    }

    private static int[] DepthPerLine(SourceText text)
    {
        var depths = new int[text.Lines.Length];
        var opensOnLine = new int[text.Lines.Length];
        var closesBeforeContent = new bool[text.Lines.Length];
        var netOnLine = new int[text.Lines.Length];

        foreach (var token in SyntaxTree.ParseTokens(text))
        {
            if (token.Kind is not (SyntaxKind.LeftCurlyToken or SyntaxKind.RightCurlyToken))
                continue;

            var line = text.GetLineIndex(token.Position);

            if (token.Kind == SyntaxKind.LeftCurlyToken)
            {
                opensOnLine[line]++;
                netOnLine[line]++;
            }
            else
            {
                netOnLine[line]--;

                // A closing brace that starts its line dedents that line itself, not just the
                // lines after it. Missing this is what left every `}` indented with the body.
                if (opensOnLine[line] == 0 && netOnLine[line] < 0)
                {
                    closesBeforeContent[line] = true;
                }
            }
        }

        var depth = 0;

        for (var i = 0; i < text.Lines.Length; i++)
        {
            var lineDepth = depth;

            if (closesBeforeContent[i])
            {
                lineDepth += netOnLine[i];
            }

            depths[i] = Math.Max(0, lineDepth);
            depth = Math.Max(0, depth + netOnLine[i]);
        }

        return depths;
    }
}
