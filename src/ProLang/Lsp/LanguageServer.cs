using System.Text.Json;
using System.Text.Json.Nodes;
using ProLang.Lsp.Handlers;
using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;

namespace ProLang.Lsp;

/// <summary>
/// The ProLang language server: reads requests, answers them from the compiler, publishes
/// diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Single-threaded by design. Every answer comes from binding a program, and binding touches
/// process-wide state — the builtin module registry and the .NET assembly registry — that is not
/// safe to enter twice at once. A request is fast enough that serialising them is not felt.
/// </para>
/// <para>
/// Every handler is wrapped. A language server that dies takes all editor support with it, so an
/// exception answering one request has to cost that request and nothing else.
/// </para>
/// </remarks>
internal sealed class LanguageServer
{
    private readonly JsonRpcConnection _connection;
    private readonly DocumentStore _documents = new();
    private readonly AnalysisService _analysis;
    private readonly List<string> _workspaceRoots = new();
    private readonly TextWriter? _log;

    private bool _shutdownRequested;

    /// <summary>
    /// Reported to the editor at start-up, and by <c>ProLang: Show Language Server Info</c>.
    /// </summary>
    /// <remarks>
    /// Read from the assembly rather than written here, so it cannot disagree with the version of
    /// the compiler it is part of — which is the first thing worth checking when an editor is
    /// behaving as though a fix is not in.
    /// </remarks>
    private static string ServerVersion =>
        typeof(LanguageServer).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public LanguageServer(Stream input, Stream output, string? stdRoot, TextWriter? log = null)
    {
        _connection = new JsonRpcConnection(input, output);
        _analysis = new AnalysisService(_documents, stdRoot);
        _log = log;
    }

    public int Run()
    {
        while (true)
        {
            var message = _connection.Read();

            if (message == null)
            {
                // The editor closed the pipe without saying goodbye, which happens whenever it is
                // killed rather than quit.
                return _shutdownRequested ? 0 : 1;
            }

            if (message.Method == null)
            {
                continue;
            }

            if (message.Method == "exit")
            {
                return _shutdownRequested ? 0 : 1;
            }

            try
            {
                Dispatch(message);
            }
            catch (Exception e)
            {
                Log($"{message.Method} failed: {e}");

                if (message.IsRequest)
                {
                    _connection.RespondWithError(message.Id, JsonRpcError.InternalError, e.Message);
                }
            }
        }
    }

    private void Dispatch(JsonRpcMessage message)
    {
        switch (message.Method)
        {
            case "initialize":
                Initialize(message);
                break;

            case "initialized":
                break;

            case "shutdown":
                _shutdownRequested = true;
                _connection.Respond(message.Id, null);
                break;

            case "textDocument/didOpen":
            {
                var parameters = Params<DidOpenTextDocumentParams>(message)!;
                var document = _documents.Open(parameters.TextDocument);

                _analysis.InvalidateDependents(document.Path);
                Publish(document);
                break;
            }

            case "textDocument/didChange":
            {
                var parameters = Params<DidChangeTextDocumentParams>(message)!;
                var document = _documents.Change(parameters);

                if (document != null)
                {
                    // Everything that imports this file has to be re-analysed too, or a change to
                    // a module is invisible to the files using it until they are touched.
                    _analysis.InvalidateDependents(document.Path);
                    Publish(document);
                }

                break;
            }

            case "textDocument/didSave":
                break;

            case "textDocument/didClose":
            {
                var parameters = Params<DidCloseTextDocumentParams>(message)!;
                var uri = parameters.TextDocument.Uri;

                _analysis.Forget(DocumentUri.ToPath(uri));
                _documents.Close(uri);

                // Diagnostics belong to an open document. Without this they stay on screen for a
                // file nobody is looking at any more.
                _connection.Notify("textDocument/publishDiagnostics", new PublishDiagnosticsParams
                {
                    Uri = uri,
                    Diagnostics = [],
                });

                break;
            }

            case "textDocument/hover":
                Answer<TextDocumentPositionParams>(message, (a, p) => NavigationHandlers.Hover(a, p.Position));
                break;

            case "textDocument/definition":
                Answer<TextDocumentPositionParams>(message, (a, p) => NavigationHandlers.Definition(a, p.Position));
                break;

            case "textDocument/references":
                Answer<ReferenceParams>(message,
                    (a, p) => NavigationHandlers.References(a, p.Position, p.Context.IncludeDeclaration));
                break;

            case "textDocument/documentHighlight":
                Answer<TextDocumentPositionParams>(message, (a, p) => NavigationHandlers.Highlight(a, p.Position));
                break;

            case "textDocument/completion":
                Answer<CompletionParams>(message, (a, p) => CompletionHandler.Complete(a, p.Position));
                break;

            case "completionItem/resolve":
                _connection.Respond(message.Id, message.Params);
                break;

            case "textDocument/signatureHelp":
                Answer<TextDocumentPositionParams>(message, (a, p) => SignatureHelpHandler.Help(a, p.Position));
                break;

            case "textDocument/documentSymbol":
                Answer<DocumentSymbolParams>(message, (a, _) => SymbolHandlers.DocumentSymbols(a));
                break;

            case "textDocument/documentLink":
                Answer<DocumentLinkParams>(message, (a, _) => NavigationHandlers.DocumentLinks(a));
                break;

            case "textDocument/semanticTokens/full":
                Answer<SemanticTokensParams>(message,
                    (a, _) => new SemanticTokens { Data = SemanticTokensHandler.Tokens(a) });
                break;

            case "textDocument/foldingRange":
                Answer<FoldingRangeParams>(message, (a, _) => FoldingHandler.Ranges(a));
                break;

            case "textDocument/inlayHint":
                Answer<InlayHintParams>(message, (a, p) => InlayHintHandler.Hints(a, p.Range));
                break;

            case "textDocument/formatting":
                Answer<DocumentFormattingParams>(message, (a, p) => FormattingHandler.Format(a, p.Options));
                break;

            case "textDocument/prepareRename":
                PrepareRename(message);
                break;

            case "textDocument/rename":
                Rename(message);
                break;

            case "workspace/symbol":
                WorkspaceSymbols(message);
                break;

            case "workspace/didChangeConfiguration":
            case "workspace/didChangeWatchedFiles":
                break;

            default:
                if (message.IsRequest)
                {
                    _connection.RespondWithError(message.Id, JsonRpcError.MethodNotFound,
                        $"'{message.Method}' is not supported by the ProLang language server.");
                }

                break;
        }
    }

    private void Initialize(JsonRpcMessage message)
    {
        var parameters = Params<Protocol.InitializeParams>(message);

        if (parameters?.WorkspaceFolders != null)
        {
            _workspaceRoots.AddRange(parameters.WorkspaceFolders.Select(f => DocumentUri.ToPath(f.Uri)));
        }
        else if (parameters?.RootUri != null)
        {
            _workspaceRoots.Add(DocumentUri.ToPath(parameters.RootUri));
        }

        // Analysis needs them too: a rename must know which files belong to the user's workspace
        // before it offers to edit them.
        _analysis.WorkspaceRoots.AddRange(_workspaceRoots.Select(DocumentUri.NormalisePath));

        _connection.Respond(message.Id, new
        {
            capabilities = new
            {
                // Incremental: a keystroke sends the character typed rather than the whole file,
                // which matters on the larger modules of the standard library.
                textDocumentSync = new { openClose = true, change = 2, save = true },
                hoverProvider = true,
                completionProvider = new
                {
                    resolveProvider = false,
                    triggerCharacters = new[] { ".", "\"", "/", ":" },
                },
                signatureHelpProvider = new { triggerCharacters = new[] { "(", "," } },
                definitionProvider = true,
                referencesProvider = true,
                documentHighlightProvider = true,
                documentSymbolProvider = true,
                workspaceSymbolProvider = true,
                documentLinkProvider = new { resolveProvider = false },
                renameProvider = new { prepareProvider = true },
                documentFormattingProvider = true,
                foldingRangeProvider = true,
                inlayHintProvider = true,
                semanticTokensProvider = new
                {
                    legend = new
                    {
                        tokenTypes = SemanticTokensHandler.TokenTypes,
                        tokenModifiers = SemanticTokensHandler.TokenModifiers,
                    },
                    full = true,
                },
            },
            serverInfo = new { name = "ProLang", version = ServerVersion },
        });

        Log($"ProLang language server ready. Standard library: {_analysis.StdRoot ?? AnalysisService.DefaultStdRoot}");
    }

    private void PrepareRename(JsonRpcMessage message)
    {
        var parameters = Params<TextDocumentPositionParams>(message)!;
        var analysis = AnalyseFor(parameters.TextDocument.Uri);

        if (analysis == null)
        {
            _connection.Respond(message.Id, null);
            return;
        }

        var (range, refusal) = NavigationHandlers.PrepareRename(analysis, parameters.Position);

        if (refusal != null)
        {
            _connection.RespondWithError(message.Id, JsonRpcError.RequestFailed, refusal);
            return;
        }

        _connection.Respond(message.Id, range);
    }

    private void Rename(JsonRpcMessage message)
    {
        var parameters = Params<RenameParams>(message)!;
        var analysis = AnalyseFor(parameters.TextDocument.Uri);

        if (analysis == null)
        {
            _connection.Respond(message.Id, null);
            return;
        }

        var (edit, refusal) = NavigationHandlers.Rename(analysis, parameters.Position, parameters.NewName);

        if (refusal != null)
        {
            _connection.RespondWithError(message.Id, JsonRpcError.RequestFailed, refusal);
            return;
        }

        _connection.Respond(message.Id, edit);
    }

    private void WorkspaceSymbols(JsonRpcMessage message)
    {
        var parameters = Params<WorkspaceSymbolParams>(message)!;

        var roots = new List<string>(_workspaceRoots);
        var stdRoot = _analysis.StdRoot ?? AnalysisService.DefaultStdRoot;

        if (Directory.Exists(stdRoot))
        {
            roots.Add(stdRoot);
        }

        var results = SymbolHandlers.WorkspaceSymbols(parameters.Query, roots,
            _documents.All().Select(d => d.Path));

        _connection.Respond(message.Id, results);
    }

    /// <summary>Answers a request about one document, or with null when it is not open.</summary>
    private void Answer<TParams>(JsonRpcMessage message, Func<Analysis, TParams, object?> handler)
        where TParams : class
    {
        var parameters = Params<TParams>(message);

        if (parameters == null)
        {
            _connection.Respond(message.Id, null);
            return;
        }

        var uri = UriOf(parameters);
        var analysis = uri == null ? null : AnalyseFor(uri);

        if (analysis == null)
        {
            _connection.Respond(message.Id, null);
            return;
        }

        try
        {
            _connection.Respond(message.Id, handler(analysis, parameters));
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // One request failing is not a reason to answer with an error the editor will show as
            // a popup. An empty answer degrades quietly, and the reason goes to the log.
            Log($"{message.Method} failed: {e}");
            _connection.Respond(message.Id, null);
        }
    }

    private Analysis? AnalyseFor(string uri)
    {
        var document = _documents.Get(uri);

        return document == null ? null : _analysis.Analyse(document);
    }

    private void Publish(OpenDocument document)
    {
        var analysis = _analysis.Analyse(document);

        _connection.Notify("textDocument/publishDiagnostics", new PublishDiagnosticsParams
        {
            Uri = document.Uri,
            Version = document.Version,
            Diagnostics = DiagnosticsHandler.ForDocument(analysis),
        });
    }

    private static string? UriOf(object parameters) => parameters switch
    {
        TextDocumentPositionParams p => p.TextDocument.Uri,
        DocumentSymbolParams p => p.TextDocument.Uri,
        DocumentFormattingParams p => p.TextDocument.Uri,
        SemanticTokensParams p => p.TextDocument.Uri,
        FoldingRangeParams p => p.TextDocument.Uri,
        InlayHintParams p => p.TextDocument.Uri,
        DocumentLinkParams p => p.TextDocument.Uri,
        _ => null,
    };

    private static T? Params<T>(JsonRpcMessage message) where T : class =>
        message.Params?.Deserialize<T>(JsonRpcConnection.SerializerOptions);

    private void Log(string message)
    {
        _log?.WriteLine(message);
        _log?.Flush();

        _connection.Notify("window/logMessage", new LogMessageParams { Type = 3, Message = message });
    }
}
