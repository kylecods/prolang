using ProLang.Compiler;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Tests.Infrastructure;

/// <summary>
/// A ProLang source with cursor positions marked in it.
/// </summary>
/// <remarks>
/// Editor behaviour is always "at this position, in this text", and writing that as two separate
/// values — a string, and a number someone counted by hand — makes a test that is unreadable and
/// wrong the moment the string is edited. A <c>|</c> in the source says where the cursor is and
/// moves with the code around it.
/// </remarks>
internal sealed class MarkedSource
{
    private const char Marker = '|';

    private MarkedSource(string text, IReadOnlyList<int> positions, string fileName)
    {
        Text = text;
        Positions = positions;
        FileName = fileName;
        SourceText = SourceText.From(text, fileName);
    }

    /// <summary>The source with the markers removed — what the compiler sees.</summary>
    public string Text { get; }

    public SourceText SourceText { get; }

    public string FileName { get; }

    /// <summary>Where each marker was, in order.</summary>
    public IReadOnlyList<int> Positions { get; }

    /// <summary>The only marked position. Throws if the source did not mark exactly one.</summary>
    public int Position => Positions.Count == 1
        ? Positions[0]
        : throw new InvalidOperationException(
            $"Expected exactly one '{Marker}' marker, found {Positions.Count}.");

    public static MarkedSource Parse(string markedText, string fileName = "test.prl")
    {
        var builder = new System.Text.StringBuilder(markedText.Length);
        var positions = new List<int>();

        foreach (var c in markedText)
        {
            if (c == Marker)
            {
                positions.Add(builder.Length);
            }
            else
            {
                builder.Append(c);
            }
        }

        return new MarkedSource(builder.ToString(), positions, fileName);
    }

    /// <summary>Binds this source, with everything the binder resolved recorded.</summary>
    public SemanticModel Bind()
    {
        var compilation = ProLangCompilation.CreateForAnalysis([], SyntaxTree.Parse(SourceText));

        return compilation.GetSemanticModel();
    }

    /// <summary>The source text covered by a span, for asserting on what a location points at.</summary>
    public string TextOf(TextSpan span) => SourceText.ToString(span);
}
