using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using LspPosition = ProLang.Lsp.Protocol.Position;
using LspRange = ProLang.Lsp.Protocol.Range;

namespace ProLang.Tests.Lsp.Infrastructure;

/// <summary>
/// Drives the language server's handlers directly, with no process and no pipe.
/// </summary>
/// <remarks>
/// The handlers are written as functions of an <see cref="Analysis"/> and a request, returning a
/// response, precisely so they can be tested like this. Everything that knows about streams lives
/// in the server loop, which has its own small test.
/// </remarks>
internal sealed class LspTestHost
{
    private readonly DocumentStore _documents = new();
    private readonly AnalysisService _analysis;
    private int _version;

    public LspTestHost(string? stdRoot = null)
    {
        _analysis = new AnalysisService(_documents, stdRoot);
    }

    /// <summary>Opens a document whose text may carry <c>|</c> cursor markers.</summary>
    public OpenDocument Open(string markedText, string fileName = "test.prl")
    {
        var path = Path.Combine(Path.GetTempPath(), "prolang-lsp-tests", fileName);
        var marked = MarkedText.Parse(markedText);

        Markers = marked.Positions;

        var document = _documents.Open(new TextDocumentItem
        {
            Uri = DocumentUri.FromPath(path),
            LanguageId = "prolang",
            Version = ++_version,
            Text = marked.Text,
        });

        Document = document;

        return document;
    }

    /// <summary>Applies an edit the way an editor would, as a ranged change.</summary>
    public void Change(LspRange range, string newText)
    {
        _documents.Change(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = Document.Uri, Version = ++_version },
            ContentChanges = [new TextDocumentContentChangeEvent { Range = range, Text = newText }],
        });

        _analysis.InvalidateDependents(Document.Path);
    }

    public OpenDocument Document { get; private set; } = null!;

    public IReadOnlyList<int> Markers { get; private set; } = [];

    public Analysis Analysis => _analysis.Analyse(Document);

    /// <summary>The single marked position, as a protocol position.</summary>
    public LspPosition Cursor => PositionAt(Markers.Single());

    public LspPosition PositionAt(int offset)
    {
        var text = Analysis.Text;
        var line = text.GetLineIndex(offset);

        return new LspPosition
        {
            Line = line,
            Character = offset - text.Lines[line].Start,
        };
    }

    public string TextOf(LspRange range)
    {
        var text = Analysis.Text;
        var start = text.GetPosition(range.Start.Line, range.Start.Character);
        var end = text.GetPosition(range.End.Line, range.End.Character);

        return text.ToString(ProLang.Text.TextSpan.FromBounds(start, end));
    }
}

/// <summary>Splits <c>|</c> markers out of a fixture.</summary>
internal static class MarkedText
{
    public static (string Text, List<int> Positions) Parse(string markedText)
    {
        var builder = new System.Text.StringBuilder(markedText.Length);
        var positions = new List<int>();

        foreach (var c in markedText)
        {
            if (c == '|')
            {
                positions.Add(builder.Length);
            }
            else
            {
                builder.Append(c);
            }
        }

        return (builder.ToString(), positions);
    }
}
