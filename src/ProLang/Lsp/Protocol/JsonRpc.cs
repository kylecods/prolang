using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ProLang.Lsp.Protocol;

/// <summary>One JSON-RPC message, in the shape the Language Server Protocol uses.</summary>
/// <remarks>
/// Request, response and notification are one type because they are one wire format distinguished
/// only by which fields are present: an <see cref="Id"/> and a <see cref="Method"/> is a request,
/// a method alone is a notification, an id alone is a response.
/// </remarks>
internal sealed class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonNode? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonNode? Params { get; set; }

    [JsonPropertyName("result")]
    public JsonNode? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    public bool IsRequest => Method != null && Id != null;

    public bool IsNotification => Method != null && Id == null;
}

internal sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    // The subset of the protocol's codes this server can actually produce.
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InternalError = -32603;
    public const int RequestFailed = -32803;
}

/// <summary>
/// Reads and writes LSP messages over a pair of streams.
/// </summary>
/// <remarks>
/// <para>
/// The base protocol is deliberately small: <c>Content-Length: N</c>, a blank line, then N bytes of
/// UTF-8 JSON. Implementing it directly is a few dozen lines and costs nothing, where the
/// established library for this would bring a dependency injection container, a mediator and a
/// logging framework into a compiler whose entire dependency list is three packages — all of which
/// would ship inside the `dotnet tool` package.
/// </para>
/// <para>
/// Headers are read a byte at a time rather than through a <see cref="StreamReader"/>. A reader
/// buffers, and would swallow part of the body that follows the headers it was asked for.
/// </para>
/// </remarks>
internal sealed class JsonRpcConnection
{
    // Serialization goes through the source-generated context. Under Native AOT the reflection
    // based resolver is disabled, so the options the helpers below use must resolve metadata from
    // LspJsonContext or serialization throws at runtime.
    private static readonly JsonSerializerOptions Options = new(LspJsonContext.Default.Options)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly object _writeLock = new();

    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    public static JsonSerializerOptions SerializerOptions => Options;

    /// <summary>Reads the next message, or null at end of input.</summary>
    public JsonRpcMessage? Read()
    {
        var contentLength = -1;

        while (true)
        {
            var header = ReadHeaderLine();

            if (header == null)
            {
                return null;
            }

            if (header.Length == 0)
            {
                break;
            }

            var separator = header.IndexOf(':');

            if (separator < 0)
            {
                continue;
            }

            var name = header[..separator].Trim();
            var value = header[(separator + 1)..].Trim();

            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var parsed))
            {
                contentLength = parsed;
            }
        }

        if (contentLength < 0)
        {
            return null;
        }

        var body = new byte[contentLength];
        var read = 0;

        while (read < contentLength)
        {
            var count = _input.Read(body, read, contentLength - read);

            if (count == 0)
            {
                return null;
            }

            read += count;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonRpcMessage>(body, Options);
        }
        catch (JsonException)
        {
            // A malformed message is not a reason to stop serving. Returning an empty one lets the
            // loop report a parse error and carry on with whatever comes next.
            return new JsonRpcMessage();
        }
    }

    public void Write(JsonRpcMessage message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {json.Length}\r\n\r\n");

        // Locked because diagnostics are published from the analysis thread while requests are
        // answered on the read loop, and two interleaved writes would corrupt both messages.
        lock (_writeLock)
        {
            _output.Write(header);
            _output.Write(json);
            _output.Flush();
        }
    }

    public void Respond(JsonNode? id, object? result)
    {
        Write(new JsonRpcMessage
        {
            Id = id,
            Result = result == null ? null : SerializeToNode(result),
        });
    }

    public void RespondWithError(JsonNode? id, int code, string message)
    {
        Write(new JsonRpcMessage
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message },
        });
    }

    public void Notify(string method, object? parameters)
    {
        Write(new JsonRpcMessage
        {
            Method = method,
            Params = parameters == null ? null : SerializeToNode(parameters),
        });
    }

    /// <summary>
    /// Serializes an arbitrary result value to a <see cref="JsonNode"/> through the source
    /// generated context.
    /// </summary>
    /// <remarks>
    /// The value's static type is <see cref="object"/>, so the serialization metadata has to be
    /// looked up by the runtime type. Every type this server actually sends — the LSP types and
    /// the collections of them — is registered on <see cref="LspJsonContext"/>, and
    /// <see cref="JsonSerializerContext.GetTypeInfo(Type)"/> resolves those registrations without
    /// reflection. A type that is not registered throws here rather than silently emitting an
    /// empty node, which is how a new response type forces its own registration.
    /// </remarks>
    private static JsonNode SerializeToNode(object value)
    {
        var runtimeType = value.GetType();

        if (runtimeType == typeof(JsonNode))
        {
            return (JsonNode)value;
        }

        var typeInfo = LspJsonContext.Default.GetTypeInfo(runtimeType)
            ?? throw new InvalidOperationException(
                $"Type '{runtimeType.FullName}' is not registered on {nameof(LspJsonContext)}; " +
                "add it to the context so the AOT build can serialize it.");

        return JsonSerializer.SerializeToNode(value, typeInfo);
    }

    private string? ReadHeaderLine()
    {
        var builder = new StringBuilder();

        while (true)
        {
            var b = _input.ReadByte();

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
