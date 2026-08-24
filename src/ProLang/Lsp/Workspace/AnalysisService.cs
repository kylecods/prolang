using ProLang.Compiler;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Lsp.Workspace;

/// <summary>
/// The result of analysing one open document.
/// </summary>
/// <remarks>
/// <see cref="Model"/> is null when binding could not run at all. The syntax tree is always
/// present, because parsing recovers from anything — which is what lets the outline, folding and
/// formatting keep working in a file that does not yet compile.
/// </remarks>
internal sealed record Analysis(
    OpenDocument Document,
    SyntaxTree SyntaxTree,
    ProLangCompilation? Compilation,
    SemanticModel? Model,
    string? StdRoot,
    IReadOnlyList<string> WorkspaceRoots)
{
    public SourceText Text => SyntaxTree.Text;

    /// <summary>
    /// Whether a file is one the user has open in this workspace.
    /// </summary>
    /// <remarks>
    /// The question a rename has to ask before it edits anything. A compilation reaches every file
    /// the program imports, and when the standard library is the one installed beside the
    /// compiler, that includes files outside the workspace entirely — which an editor must not
    /// rewrite on the strength of a rename in an unrelated project.
    /// </remarks>
    public bool IsInWorkspace(string path)
    {
        if (WorkspaceRoots.Count == 0)
        {
            // No workspace was declared, so the only file that can be edited is the open one.
            return string.Equals(DocumentUri.NormalisePath(path), Document.Path, StringComparison.OrdinalIgnoreCase);
        }

        var full = DocumentUri.NormalisePath(path);

        return WorkspaceRoots.Any(root =>
            full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Turns the open documents into compilations, and remembers the answers.
/// </summary>
/// <remarks>
/// <para>
/// Analysis is single-threaded on purpose. <c>BuiltInModule</c>'s registry and the .NET assembly
/// registry are process-wide mutable state — the test project already disables parallelism because
/// of it — so binding two documents at once would race on both.
/// </para>
/// <para>
/// Each open file is its own compilation root, because ProLang has no project file: what a program
/// consists of is decided by what it imports. Two open files that both import <c>std/util</c>
/// therefore bind it twice. That is accepted rather than solved, because solving it means
/// inventing a project system.
/// </para>
/// </remarks>
internal sealed class AnalysisService
{
    private readonly DocumentStore _documents;
    private readonly ISourceTextProvider _provider;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public AnalysisService(DocumentStore documents, string? stdRoot)
    {
        _documents = documents;
        _provider = new BufferProvider(documents);
        StdRoot = stdRoot;
    }

    /// <summary>The folders the editor has open. Set once, at initialisation.</summary>
    public List<string> WorkspaceRoots { get; } = new();

    /// <summary>Where library imports resolve from, or null for the compiler's own directory.</summary>
    public string? StdRoot { get; }

    /// <summary>
    /// The <c>std</c> directory shipped beside the compiler.
    /// </summary>
    /// <remarks>
    /// Correct for an installed compiler and for one built from the repository alike, because
    /// <c>ProLang.csproj</c> copies the library into the build output. It is only wrong when
    /// someone has the ProLang repository itself open and wants navigation to land in the files
    /// they can edit rather than in the copy under <c>bin/</c> — which is what
    /// <see cref="StdRoot"/> overrides.
    /// </remarks>
    public static string DefaultStdRoot { get; } = Path.Combine(AppContext.BaseDirectory, "std");

    /// <summary>Analyses a document, reusing the previous result when nothing has changed.</summary>
    public Analysis Analyse(OpenDocument document)
    {
        lock (_lock)
        {
            // Version alone is enough here because a change to any *other* file in this graph
            // removes this entry outright — see InvalidateDependents, which every didChange calls.
            if (_cache.TryGetValue(document.Path, out var cached)
                && cached.Version == document.Version
                && ReferenceEquals(cached.Analysis.Document, document))
            {
                return cached.Analysis;
            }
        }

        var tree = SyntaxTree.Parse(document.SourceText);

        ProLangCompilation? compilation = null;
        SemanticModel? model = null;

        try
        {
            compilation = ProLangCompilation.CreateForAnalysis([], _provider, StdRoot, tree);
            model = compilation.GetSemanticModel();
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // Import resolution and binding both have paths that throw on input no one would ever
            // compile but someone will certainly type. The document still has a syntax tree, and
            // everything driven by that keeps working.
        }

        var analysis = new Analysis(document, tree, compilation, model, StdRoot ?? DefaultStdRoot, WorkspaceRoots);

        lock (_lock)
        {
            _cache[document.Path] = new CacheEntry(document.Version, analysis, Dependencies(compilation));
        }

        return analysis;
    }

    public void Forget(string path)
    {
        lock (_lock)
        {
            _cache.Remove(DocumentUri.NormalisePath(path));
        }
    }

    /// <summary>Invalidates every cached analysis that read <paramref name="path"/>.</summary>
    /// <remarks>
    /// Editing a module has to re-analyse everything that imports it, or the file being edited is
    /// the only one that ever notices the change.
    /// </remarks>
    public void InvalidateDependents(string path)
    {
        var normalised = DocumentUri.NormalisePath(path);

        lock (_lock)
        {
            var stale = _cache
                .Where(e => e.Value.Dependencies.Contains(normalised))
                .Select(e => e.Key)
                .ToList();

            foreach (var key in stale)
            {
                _cache.Remove(key);
            }
        }
    }

    /// <summary>Every file that went into a compilation — its root and everything it imported.</summary>
    private static HashSet<string> Dependencies(ProLangCompilation? compilation)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (compilation == null)
        {
            return paths;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!string.IsNullOrEmpty(tree.Text.FileName))
            {
                paths.Add(DocumentUri.NormalisePath(tree.Text.FileName));
            }
        }

        return paths;
    }

    private sealed record CacheEntry(int Version, Analysis Analysis, HashSet<string> Dependencies);

    /// <summary>Serves the editor's buffers to the compiler's import resolution.</summary>
    private sealed class BufferProvider : ISourceTextProvider
    {
        private readonly DocumentStore _documents;

        public BufferProvider(DocumentStore documents) => _documents = documents;

        public bool Exists(string fullPath) => _documents.TryGetText(fullPath, out _);

        public bool TryGetSourceText(string fullPath, out SourceText text)
        {
            if (_documents.TryGetText(fullPath, out var buffered))
            {
                text = SourceText.From(buffered, DocumentUri.NormalisePath(fullPath));
                return true;
            }

            text = null!;
            return false;
        }
    }
}
