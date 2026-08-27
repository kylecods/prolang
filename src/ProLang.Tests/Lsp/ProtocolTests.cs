using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProLang.Lsp;
using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using LspPosition = ProLang.Lsp.Protocol.Position;
using LspRange = ProLang.Lsp.Protocol.Range;

namespace ProLang.Tests.Lsp;

/// <summary>
/// The transport, the document store, and one run of the whole server over a pipe.
/// </summary>
public class ProtocolTests
{
    private static byte[] Frame(string json) =>
        Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}");

    [Fact]
    public void Connection_ReadsAFramedMessage()
    {
        var input = new MemoryStream(Frame("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}"""));
        var connection = new JsonRpcConnection(input, new MemoryStream());

        var message = connection.Read();

        Assert.Equal("initialize", message!.Method);
        Assert.True(message.IsRequest);
    }

    [Fact]
    public void Connection_ReadsSeveralMessagesFromOneBuffer()
    {
        var buffer = new MemoryStream();
        buffer.Write(Frame("""{"jsonrpc":"2.0","method":"a"}"""));
        buffer.Write(Frame("""{"jsonrpc":"2.0","method":"b"}"""));
        buffer.Position = 0;

        var connection = new JsonRpcConnection(buffer, new MemoryStream());

        Assert.Equal("a", connection.Read()!.Method);
        Assert.Equal("b", connection.Read()!.Method);
        Assert.Null(connection.Read());
    }

    /// <summary>
    /// The length is in bytes, which only shows up when the body is not ASCII.
    /// </summary>
    [Fact]
    public void Connection_MeasuresContentLengthInBytes()
    {
        var input = new MemoryStream(Frame("""{"jsonrpc":"2.0","method":"a","params":{"text":"✓ — é"}}"""));
        var connection = new JsonRpcConnection(input, new MemoryStream());

        var message = connection.Read();

        Assert.Equal("✓ — é", message!.Params!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Connection_IgnoresUnknownHeaders()
    {
        var json = """{"jsonrpc":"2.0","method":"a"}""";
        var framed = $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\nContent-Length: {json.Length}\r\n\r\n{json}";

        var connection = new JsonRpcConnection(new MemoryStream(Encoding.UTF8.GetBytes(framed)), new MemoryStream());

        Assert.Equal("a", connection.Read()!.Method);
    }

    /// <summary>Malformed JSON is survivable; the connection has to stay usable.</summary>
    [Fact]
    public void Connection_DoesNotThrowOnMalformedJson()
    {
        var buffer = new MemoryStream();
        buffer.Write(Frame("{ not json"));
        buffer.Write(Frame("""{"jsonrpc":"2.0","method":"after"}"""));
        buffer.Position = 0;

        var connection = new JsonRpcConnection(buffer, new MemoryStream());

        Assert.Null(connection.Read()!.Method);
        Assert.Equal("after", connection.Read()!.Method);
    }

    [Fact]
    public void Connection_WritesAFramedResponse()
    {
        var output = new MemoryStream();
        var connection = new JsonRpcConnection(new MemoryStream(), output);

        // A registered LSP type, not an anonymous object: the connection serializes through the
        // source-generated context, which has no metadata for anonymous types.
        connection.Respond(JsonValue.Create(7), new Hover
        {
            Contents = new MarkupContent { Kind = "markdown", Value = "x" },
        });

        var written = Encoding.UTF8.GetString(output.ToArray());

        Assert.StartsWith("Content-Length: ", written);
        Assert.Contains("\r\n\r\n", written);
        Assert.Contains("\"value\":\"x\"", written);
    }

    [Fact]
    public void DocumentUri_RoundTripsAPath()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "a b", "c.prl"));

        var uri = DocumentUri.FromPath(path);

        Assert.StartsWith("file:///", uri);
        Assert.Equal(path, DocumentUri.NormalisePath(DocumentUri.ToPath(uri)));
    }

    [Theory]
    [InlineData("file:///d:/a/b.prl")]
    [InlineData("file:///D%3A/a/b.prl")]
    public void DocumentUri_AcceptsBothSpellingsOfADriveLetter(string uri)
    {
        var path = DocumentUri.ToPath(uri);

        Assert.DoesNotContain("%3A", path);
        Assert.DoesNotContain(":/a", path.Replace(":\\a", string.Empty));
    }

    /// <summary>
    /// An incremental change replaces exactly the range the editor named.
    /// </summary>
    /// <remarks>
    /// Incremental sync is what keeps a keystroke from re-sending a whole module. It is also where
    /// an off-by-one silently corrupts a buffer and every later answer with it, so it is worth
    /// pinning directly.
    /// </remarks>
    [Fact]
    public void DocumentStore_AppliesAnIncrementalChange()
    {
        var store = new DocumentStore();
        var uri = DocumentUri.FromPath(Path.Combine(Path.GetTempPath(), "sync.prl"));

        store.Open(new TextDocumentItem { Uri = uri, Version = 1, Text = "func main() {\n    let x = 1\n}" });

        store.Change(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges =
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new LspRange
                    {
                        Start = new LspPosition { Line = 1, Character = 8 },
                        End = new LspPosition { Line = 1, Character = 9 },
                    },
                    Text = "count",
                },
            ],
        });

        Assert.Equal("func main() {\n    let count = 1\n}", store.Get(uri)!.Text);
        Assert.Equal(2, store.Get(uri)!.Version);
    }

    [Fact]
    public void DocumentStore_AppliesSeveralChangesInOrder()
    {
        var store = new DocumentStore();
        var uri = DocumentUri.FromPath(Path.Combine(Path.GetTempPath(), "sync2.prl"));

        store.Open(new TextDocumentItem { Uri = uri, Version = 1, Text = "ab" });

        store.Change(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges =
            [
                Insert(0, 1, "X"),
                Insert(0, 3, "Y"),
            ],
        });

        // "ab" → "aXb" → "aXbY": the second change is resolved against the result of the first.
        Assert.Equal("aXbY", store.Get(uri)!.Text);
    }

    [Fact]
    public void DocumentStore_ClampsAnOutOfRangeChange()
    {
        var store = new DocumentStore();
        var uri = DocumentUri.FromPath(Path.Combine(Path.GetTempPath(), "sync3.prl"));

        store.Open(new TextDocumentItem { Uri = uri, Version = 1, Text = "abc" });

        store.Change(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges = [Insert(99, 99, "!")],
        });

        Assert.Equal("abc!", store.Get(uri)!.Text);
    }

    [Fact]
    public void DocumentStore_ReplacesTheWholeDocumentWhenNoRangeIsGiven()
    {
        var store = new DocumentStore();
        var uri = DocumentUri.FromPath(Path.Combine(Path.GetTempPath(), "sync4.prl"));

        store.Open(new TextDocumentItem { Uri = uri, Version = 1, Text = "old" });

        store.Change(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges = [new TextDocumentContentChangeEvent { Range = null, Text = "new" }],
        });

        Assert.Equal("new", store.Get(uri)!.Text);
    }

    /// <summary>
    /// The whole server, over a pipe, from initialize to exit.
    /// </summary>
    /// <remarks>
    /// The handler tests bypass the transport entirely, so this is what says the two halves fit
    /// together: that capabilities are announced, that opening a document publishes diagnostics
    /// unasked, that closing one clears them, and that the process would exit cleanly.
    /// </remarks>
    [Fact]
    public void Server_RunsAFullSession()
    {
        var uri = DocumentUri.FromPath(Path.Combine(Path.GetTempPath(), "session.prl"));

        var input = new MemoryStream();
        input.Write(Frame("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}"""));
        input.Write(Frame(
            "{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{\"textDocument\":{\"uri\":\""
            + uri + "\",\"languageId\":\"prolang\",\"version\":1,\"text\":\"func main() { let x: int = missing }\"}}}"));
        input.Write(Frame(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"textDocument/documentSymbol\",\"params\":{\"textDocument\":{\"uri\":\""
            + uri + "\"}}}"));
        input.Write(Frame(
            "{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didClose\",\"params\":{\"textDocument\":{\"uri\":\""
            + uri + "\"}}}"));
        input.Write(Frame("""{"jsonrpc":"2.0","id":3,"method":"shutdown"}"""));
        input.Write(Frame("""{"jsonrpc":"2.0","method":"exit"}"""));
        input.Position = 0;

        var output = new MemoryStream();

        var exitCode = new LanguageServer(input, output, stdRoot: null).Run();

        Assert.Equal(0, exitCode);

        var messages = ReadAll(output);

        var initialize = messages.First(m => m["id"]?.GetValue<int>() == 1);
        Assert.True(initialize["result"]!["capabilities"]!["hoverProvider"]!.GetValue<bool>());

        var published = messages.Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics").ToList();

        // One for the open document, and one clearing them when it closed.
        Assert.Equal(2, published.Count);
        Assert.NotEmpty(published[0]["params"]!["diagnostics"]!.AsArray());
        Assert.Empty(published[1]["params"]!["diagnostics"]!.AsArray());

        var symbols = messages.First(m => m["id"]?.GetValue<int>() == 2);
        Assert.Equal("main", symbols["result"]!.AsArray()[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Server_ReportsAnUnknownMethodRatherThanFailing()
    {
        var input = new MemoryStream();
        input.Write(Frame("""{"jsonrpc":"2.0","id":1,"method":"textDocument/somethingElse","params":{}}"""));
        input.Write(Frame("""{"jsonrpc":"2.0","id":2,"method":"shutdown"}"""));
        input.Write(Frame("""{"jsonrpc":"2.0","method":"exit"}"""));
        input.Position = 0;

        var output = new MemoryStream();

        Assert.Equal(0, new LanguageServer(input, output, stdRoot: null).Run());

        var error = ReadAll(output).First(m => m["id"]?.GetValue<int>() == 1)["error"];

        Assert.Equal(-32601, error!["code"]!.GetValue<int>());
    }

    private static TextDocumentContentChangeEvent Insert(int line, int character, string text) => new()
    {
        Range = new LspRange
        {
            Start = new LspPosition { Line = line, Character = character },
            End = new LspPosition { Line = line, Character = character },
        },
        Text = text,
    };

    private static List<JsonObject> ReadAll(MemoryStream output)
    {
        output.Position = 0;

        var connection = new JsonRpcConnection(output, new MemoryStream());
        var messages = new List<JsonObject>();

        while (true)
        {
            var raw = ReadRaw(output);

            if (raw == null)
            {
                return messages;
            }

            messages.Add(JsonNode.Parse(raw)!.AsObject());
        }
    }

    private static string? ReadRaw(Stream stream)
    {
        var length = -1;

        while (true)
        {
            var line = ReadLine(stream);

            if (line == null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim());
            }
        }

        if (length < 0)
        {
            return null;
        }

        var body = new byte[length];
        var read = 0;

        while (read < length)
        {
            var count = stream.Read(body, read, length - read);

            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static string? ReadLine(Stream stream)
    {
        var builder = new StringBuilder();

        while (true)
        {
            var b = stream.ReadByte();

            if (b == -1)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            if (b == '\n')
            {
                return builder.ToString().TrimEnd('\r');
            }

            builder.Append((char)b);
        }
    }
}
