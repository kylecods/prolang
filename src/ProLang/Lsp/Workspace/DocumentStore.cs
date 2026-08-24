using System.Text;
using ProLang.Lsp.Protocol;
using ProLang.Text;

namespace ProLang.Lsp.Workspace;

/// <summary>One file open in the editor.</summary>
internal sealed class OpenDocument
{
    public OpenDocument(string uri, string path, int version, string text)
    {
        Uri = uri;
        Path = path;
        Version = version;
        Text = text;
    }

    public string Uri { get; }

    public string Path { get; }

    public int Version { get; set; }

    public string Text { get; set; }

    private SourceText? _sourceText;

    /// <summary>
    /// The current text as the compiler sees it.
    /// </summary>
    /// <remarks>
    /// Rebuilt lazily rather than on every keystroke, because constructing one scans the whole
    /// text for line breaks and several edits often arrive before anything asks for the result.
    /// </remarks>
    public SourceText SourceText => _sourceText ??= SourceText.From(Text, Path);

    public void SetText(string text, int version)
    {
        Text = text;
        Version = version;
        _sourceText = null;
    }
}

/// <summary>
/// The files the editor has open, and their current contents.
/// </summary>
/// <remarks>
/// This is the difference between analysing what the user is looking at and analysing what was
/// last saved to disk. It matters beyond the file being edited: a program imports its own modules,
/// so unsaved edits in one file have to be visible when another is analysed, which is what
/// <see cref="TryGetText"/> is for.
/// </remarks>
internal sealed class DocumentStore
{
    private readonly Dictionary<string, OpenDocument> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    public OpenDocument Open(TextDocumentItem item)
    {
        var path = DocumentUri.NormalisePath(DocumentUri.ToPath(item.Uri));
        var document = new OpenDocument(item.Uri, path, item.Version, item.Text);

        lock (_lock)
        {
            _byPath[path] = document;
        }

        return document;
    }

    public void Close(string uri)
    {
        var path = DocumentUri.NormalisePath(DocumentUri.ToPath(uri));

        lock (_lock)
        {
            _byPath.Remove(path);
        }
    }

    public OpenDocument? Get(string uri)
    {
        var path = DocumentUri.NormalisePath(DocumentUri.ToPath(uri));

        lock (_lock)
        {
            return _byPath.GetValueOrDefault(path);
        }
    }

    public IReadOnlyList<OpenDocument> All()
    {
        lock (_lock)
        {
            return _byPath.Values.ToList();
        }
    }

    /// <summary>The editor's version of a file, for the compiler's import resolution to prefer.</summary>
    public bool TryGetText(string path, out string text)
    {
        var normalised = DocumentUri.NormalisePath(path);

        lock (_lock)
        {
            if (_byPath.TryGetValue(normalised, out var document))
            {
                text = document.Text;
                return true;
            }
        }

        text = string.Empty;
        return false;
    }

    /// <summary>Applies the changes from one <c>didChange</c> notification.</summary>
    public OpenDocument? Change(DidChangeTextDocumentParams parameters)
    {
        var document = Get(parameters.TextDocument.Uri);

        if (document == null)
        {
            return null;
        }

        var text = document.Text;

        foreach (var change in parameters.ContentChanges)
        {
            text = change.Range == null
                ? change.Text
                : ApplyRange(text, document.Path, change);
        }

        document.SetText(text, parameters.TextDocument.Version);

        return document;
    }

    /// <summary>
    /// Replaces one range of the text.
    /// </summary>
    /// <remarks>
    /// Each change is resolved against the text as it stands after the previous one, which is what
    /// the protocol specifies — so the <see cref="SourceText"/> used to convert line and character
    /// into an offset has to be rebuilt per change rather than taken once at the start.
    /// </remarks>
    private static string ApplyRange(string text, string path, TextDocumentContentChangeEvent change)
    {
        var sourceText = SourceText.From(text, path);

        var start = sourceText.GetPosition(change.Range!.Start.Line, change.Range.Start.Character);
        var end = sourceText.GetPosition(change.Range.End.Line, change.Range.End.Character);

        // A client that is a keystroke ahead can describe a range that no longer makes sense.
        // Clamping keeps the buffer coherent; throwing would desynchronise it permanently.
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);

        return new StringBuilder(text.Length - (end - start) + change.Text.Length)
            .Append(text, 0, start)
            .Append(change.Text)
            .Append(text, end, text.Length - end)
            .ToString();
    }
}
