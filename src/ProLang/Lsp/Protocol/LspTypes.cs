using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ProLang.Lsp.Protocol;

/// <summary>
/// The Language Server Protocol types this server exchanges.
/// </summary>
/// <remarks>
/// Only the ones actually sent or received. Transcribing the whole specification would be a large
/// amount of code that nothing constructs, and every field present here is one the server fills in
/// or reads.
/// </remarks>
internal sealed class Position
{
    public int Line { get; set; }
    public int Character { get; set; }
}

internal sealed class Range
{
    public Position Start { get; set; } = new();
    public Position End { get; set; } = new();
}

internal sealed class Location
{
    public string Uri { get; set; } = string.Empty;
    public Range Range { get; set; } = new();
}

internal sealed class TextDocumentIdentifier
{
    public string Uri { get; set; } = string.Empty;
}

internal sealed class TextDocumentItem
{
    public string Uri { get; set; } = string.Empty;
    public string LanguageId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Text { get; set; } = string.Empty;
}

internal sealed class VersionedTextDocumentIdentifier
{
    public string Uri { get; set; } = string.Empty;
    public int Version { get; set; }
}

internal sealed class TextDocumentContentChangeEvent
{
    /// <summary>Null for a whole-document replacement.</summary>
    public Range? Range { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class DidOpenTextDocumentParams
{
    public TextDocumentItem TextDocument { get; set; } = new();
}

internal sealed class DidChangeTextDocumentParams
{
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();
    public List<TextDocumentContentChangeEvent> ContentChanges { get; set; } = new();
}

internal sealed class DidCloseTextDocumentParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class DidSaveTextDocumentParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal class TextDocumentPositionParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Position Position { get; set; } = new();
}

internal sealed class ReferenceParams : TextDocumentPositionParams
{
    public ReferenceContext Context { get; set; } = new();
}

internal sealed class ReferenceContext
{
    public bool IncludeDeclaration { get; set; } = true;
}

internal sealed class RenameParams : TextDocumentPositionParams
{
    public string NewName { get; set; } = string.Empty;
}

internal sealed class CompletionParams : TextDocumentPositionParams
{
    public CompletionContext? Context { get; set; }
}

internal sealed class CompletionContext
{
    public int TriggerKind { get; set; }
    public string? TriggerCharacter { get; set; }
}

internal sealed class DocumentSymbolParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class DocumentFormattingParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public FormattingOptions Options { get; set; } = new();
}

internal sealed class FormattingOptions
{
    public int TabSize { get; set; } = 4;
    public bool InsertSpaces { get; set; } = true;
}

internal sealed class WorkspaceSymbolParams
{
    public string Query { get; set; } = string.Empty;
}

internal sealed class SemanticTokensParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class SemanticTokens
{
    public List<int> Data { get; set; } = new();
}

internal sealed class FoldingRangeParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class FoldingRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Kind { get; set; }
}

internal sealed class InlayHintParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Range Range { get; set; } = new();
}

internal sealed class InlayHint
{
    public Position Position { get; set; } = new();
    public string Label { get; set; } = string.Empty;

    /// <summary>1 is a type hint, 2 a parameter hint.</summary>
    public int Kind { get; set; }

    public bool PaddingLeft { get; set; }
    public bool PaddingRight { get; set; }
}

internal sealed class TextEdit
{
    public Range Range { get; set; } = new();
    public string NewText { get; set; } = string.Empty;
}

internal sealed class WorkspaceEdit
{
    public Dictionary<string, List<TextEdit>> Changes { get; set; } = new();
}

internal sealed class MarkupContent
{
    public string Kind { get; set; } = "markdown";
    public string Value { get; set; } = string.Empty;
}

internal sealed class Hover
{
    public MarkupContent Contents { get; set; } = new();
    public Range? Range { get; set; }
}

internal sealed class CompletionItem
{
    public string Label { get; set; } = string.Empty;
    public int Kind { get; set; }
    public string? Detail { get; set; }
    public MarkupContent? Documentation { get; set; }
    public string? InsertText { get; set; }
    public string? SortText { get; set; }
    public string? FilterText { get; set; }
    public List<TextEdit>? AdditionalTextEdits { get; set; }
}

/// <summary>The subset of <c>CompletionItemKind</c> this server produces.</summary>
internal static class CompletionItemKind
{
    public const int Method = 2;
    public const int Function = 3;
    public const int Field = 5;
    public const int Variable = 6;
    public const int Module = 9;
    public const int Property = 10;
    public const int Enum = 13;
    public const int Keyword = 14;
    public const int File = 17;
    public const int EnumMember = 20;
    public const int Struct = 22;
    public const int TypeParameter = 25;
}

internal static class SymbolKinds
{
    public const int File = 1;
    public const int Module = 2;
    public const int Function = 12;
    public const int Variable = 13;
    public const int Constant = 14;
    public const int Enum = 10;
    public const int EnumMember = 22;
    public const int Struct = 23;
    public const int Field = 8;
    public const int Method = 6;
}

internal sealed class DocumentSymbol
{
    public string Name { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int Kind { get; set; }
    public Range Range { get; set; } = new();
    public Range SelectionRange { get; set; } = new();
    public List<DocumentSymbol>? Children { get; set; }
}

internal sealed class SymbolInformation
{
    public string Name { get; set; } = string.Empty;
    public int Kind { get; set; }
    public Location Location { get; set; } = new();
    public string? ContainerName { get; set; }
}

internal sealed class SignatureHelp
{
    public List<SignatureInformation> Signatures { get; set; } = new();
    public int ActiveSignature { get; set; }
    public int ActiveParameter { get; set; }
}

internal sealed class SignatureInformation
{
    public string Label { get; set; } = string.Empty;
    public MarkupContent? Documentation { get; set; }
    public List<ParameterInformation> Parameters { get; set; } = new();
}

internal sealed class ParameterInformation
{
    public string Label { get; set; } = string.Empty;
    public MarkupContent? Documentation { get; set; }
}

internal sealed class Diagnostic
{
    public Range Range { get; set; } = new();

    /// <summary>1 error, 2 warning, 3 information, 4 hint.</summary>
    public int Severity { get; set; } = 1;

    public string Source { get; set; } = "prolang";
    public string Message { get; set; } = string.Empty;
}

internal sealed class PublishDiagnosticsParams
{
    public string Uri { get; set; } = string.Empty;
    public int? Version { get; set; }
    public List<Diagnostic> Diagnostics { get; set; } = new();
}

internal sealed class LogMessageParams
{
    /// <summary>1 error, 2 warning, 3 info, 4 log.</summary>
    public int Type { get; set; } = 3;

    public string Message { get; set; } = string.Empty;
}

internal sealed class DocumentLink
{
    public Range Range { get; set; } = new();
    public string? Target { get; set; }
    public string? Tooltip { get; set; }
}

internal sealed class DocumentLinkParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class DocumentHighlight
{
    public Range Range { get; set; } = new();

    /// <summary>1 text, 2 read, 3 write.</summary>
    public int Kind { get; set; } = 1;
}

internal sealed class InitializeParams
{
    public JsonNode? Capabilities { get; set; }
    public string? RootUri { get; set; }
    public List<WorkspaceFolder>? WorkspaceFolders { get; set; }
}

internal sealed class WorkspaceFolder
{
    public string Uri { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
