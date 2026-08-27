namespace ProLang.Lsp.Protocol;

/// <summary>The <c>initialize</c> result, per the LSP specification.</summary>
internal sealed class InitializeResult
{
    public ServerCapabilities Capabilities { get; set; } = new();
    public ServerInfo? ServerInfo { get; set; }
}

/// <summary>The capabilities a server advertises during <c>initialize</c>.</summary>
internal sealed class ServerCapabilities
{
    public TextDocumentSyncOptions? TextDocumentSync { get; set; }
    public bool? HoverProvider { get; set; }
    public CompletionOptions? CompletionProvider { get; set; }
    public SignatureHelpOptions? SignatureHelpProvider { get; set; }
    public bool? DefinitionProvider { get; set; }
    public bool? ReferencesProvider { get; set; }
    public bool? DocumentHighlightProvider { get; set; }
    public bool? DocumentSymbolProvider { get; set; }
    public bool? WorkspaceSymbolProvider { get; set; }
    public DocumentLinkOptions? DocumentLinkProvider { get; set; }
    public RenameOptions? RenameProvider { get; set; }
    public bool? DocumentFormattingProvider { get; set; }
    public bool? FoldingRangeProvider { get; set; }
    public bool? InlayHintProvider { get; set; }
    public SemanticTokensOptions? SemanticTokensProvider { get; set; }
}

internal sealed class TextDocumentSyncOptions
{
    public bool OpenClose { get; set; }
    public int Change { get; set; }
    public bool Save { get; set; }
}

internal sealed class CompletionOptions
{
    public bool ResolveProvider { get; set; }
    public string[]? TriggerCharacters { get; set; }
}

internal sealed class SignatureHelpOptions
{
    public string[]? TriggerCharacters { get; set; }
}

internal sealed class DocumentLinkOptions
{
    public bool ResolveProvider { get; set; }
}

internal sealed class RenameOptions
{
    public bool PrepareProvider { get; set; }
}

internal sealed class SemanticTokensOptions
{
    public SemanticTokensLegend Legend { get; set; } = new();
    public bool Full { get; set; }
}

internal sealed class SemanticTokensLegend
{
    public string[] TokenTypes { get; set; } = [];
    public string[] TokenModifiers { get; set; } = [];
}

internal sealed class ServerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}