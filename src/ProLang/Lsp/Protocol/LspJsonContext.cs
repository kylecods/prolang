using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ProLang.Lsp.Protocol;

/// <summary>
/// Source-generated JSON metadata for the Language Server Protocol types.
/// </summary>
/// <remarks>
/// <para>
/// Under Native AOT the reflection-based serializer is unavailable, so every type that flows
/// through <see cref="JsonRpcConnection"/> is listed here and the connection serializes with this
/// context's <see cref="JsonSerializerOptions"/>.
/// </para>
/// <para>
/// <see cref="JsonNode"/> values (message <c>id</c>, <c>params</c>, <c>result</c>) are handled by
/// the JsonNodeConverter and need no per-type metadata, which is why the envelope and every typed
/// params/result class are listed but arbitrary <c>object</c> results are not — the LSP only ever
/// exchanges these types plus <see cref="JsonNode"/>.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = false)]
[JsonSerializable(typeof(JsonRpcMessage))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(Position))]
[JsonSerializable(typeof(Range))]
[JsonSerializable(typeof(Location))]
[JsonSerializable(typeof(TextDocumentIdentifier))]
[JsonSerializable(typeof(TextDocumentItem))]
[JsonSerializable(typeof(VersionedTextDocumentIdentifier))]
[JsonSerializable(typeof(TextDocumentContentChangeEvent))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(DidSaveTextDocumentParams))]
[JsonSerializable(typeof(TextDocumentPositionParams))]
[JsonSerializable(typeof(ReferenceParams))]
[JsonSerializable(typeof(ReferenceContext))]
[JsonSerializable(typeof(RenameParams))]
[JsonSerializable(typeof(CompletionParams))]
[JsonSerializable(typeof(CompletionContext))]
[JsonSerializable(typeof(DocumentSymbolParams))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(FormattingOptions))]
[JsonSerializable(typeof(WorkspaceSymbolParams))]
[JsonSerializable(typeof(SemanticTokensParams))]
[JsonSerializable(typeof(SemanticTokens))]
[JsonSerializable(typeof(FoldingRangeParams))]
[JsonSerializable(typeof(FoldingRange))]
[JsonSerializable(typeof(InlayHintParams))]
[JsonSerializable(typeof(InlayHint))]
[JsonSerializable(typeof(TextEdit))]
[JsonSerializable(typeof(WorkspaceEdit))]
[JsonSerializable(typeof(MarkupContent))]
[JsonSerializable(typeof(Hover))]
[JsonSerializable(typeof(CompletionItem))]
[JsonSerializable(typeof(DocumentSymbol))]
[JsonSerializable(typeof(SymbolInformation))]
[JsonSerializable(typeof(SignatureHelp))]
[JsonSerializable(typeof(SignatureInformation))]
[JsonSerializable(typeof(ParameterInformation))]
[JsonSerializable(typeof(Diagnostic))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(LogMessageParams))]
[JsonSerializable(typeof(DocumentLink))]
[JsonSerializable(typeof(DocumentLinkParams))]
[JsonSerializable(typeof(DocumentHighlight))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(WorkspaceFolder))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(TextDocumentSyncOptions))]
[JsonSerializable(typeof(CompletionOptions))]
[JsonSerializable(typeof(SignatureHelpOptions))]
[JsonSerializable(typeof(DocumentLinkOptions))]
[JsonSerializable(typeof(RenameOptions))]
[JsonSerializable(typeof(SemanticTokensOptions))]
[JsonSerializable(typeof(SemanticTokensLegend))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(List<CompletionItem>))]
[JsonSerializable(typeof(List<DocumentSymbol>))]
[JsonSerializable(typeof(List<SymbolInformation>))]
[JsonSerializable(typeof(List<DocumentLink>))]
[JsonSerializable(typeof(List<DocumentHighlight>))]
[JsonSerializable(typeof(List<FoldingRange>))]
[JsonSerializable(typeof(List<InlayHint>))]
[JsonSerializable(typeof(List<TextEdit>))]
[JsonSerializable(typeof(List<Location>))]
[JsonSerializable(typeof(List<Diagnostic>))]
[JsonSerializable(typeof(List<TextDocumentContentChangeEvent>))]
[JsonSerializable(typeof(List<WorkspaceFolder>))]
[JsonSerializable(typeof(List<ParameterInformation>))]
[JsonSerializable(typeof(List<SignatureInformation>))]
[JsonSerializable(typeof(Dictionary<string, List<TextEdit>>))]
internal sealed partial class LspJsonContext : JsonSerializerContext;
