using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Spike.Core.Bridge;

/// <summary>Thrown by a handler to reject a request with a message the UI may show.</summary>
public sealed class BridgeRejectException(string message) : Exception(message);

/// <summary>
/// Host side of the UI bridge. The renderer is untrusted: only registered message types are accepted,
/// payloads must match their record exactly, and anything else is rejected without reaching a handler.
/// </summary>
public sealed class BridgeDispatcher
{
    private const int MaxMessageChars = 1024 * 1024;

    // Handlers finish all their work (and any throwing) before returning a writer, so a failure never
    // leaves a half-written response.
    private delegate ValueTask<Action<Utf8JsonWriter>> Handler(JsonElement payload);

    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);
    private readonly Action<string> _post;

    public BridgeDispatcher(Action<string> post) => _post = post;

    public event Action<string, Exception>? UnexpectedError;

    public void Handle<TReq, TRes>(string type, JsonTypeInfo<TReq> request, JsonTypeInfo<TRes> response, Func<TReq, ValueTask<TRes>> handler)
    {
        _handlers.Add(type, async payload =>
        {
            var req = payload.Deserialize(request) ?? throw new BridgeRejectException("payload is null");
            var res = await handler(req).ConfigureAwait(true);
            return writer => JsonSerializer.Serialize(writer, res, response);
        });
    }

    public void Handle<TReq, TRes>(string type, JsonTypeInfo<TReq> request, JsonTypeInfo<TRes> response, Func<TReq, TRes> handler) =>
        Handle(type, request, response, r => ValueTask.FromResult(handler(r)));

    public void PostEvent<T>(string name, T payload, JsonTypeInfo<T> info)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("event", name);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, info);
            writer.WriteEndObject();
        }
        _post(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    public async Task DispatchAsync(string? raw)
    {
        if (raw is null || raw.Length > MaxMessageChars) return;

        int id;
        string type;
        JsonElement payload;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!TryReadEnvelope(doc.RootElement, out id, out type, out payload)) return;
            payload = payload.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        Action<Utf8JsonWriter>? writeResult = null;
        string? error = null;
        if (!_handlers.TryGetValue(type, out var handler))
        {
            error = $"unknown message type '{type}'";
        }
        else
        {
            try
            {
                writeResult = await handler(payload).ConfigureAwait(true);
            }
            catch (JsonException ex)
            {
                error = "invalid payload: " + ex.Message;
            }
            catch (BridgeRejectException ex)
            {
                error = ex.Message;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Host-side detail stays on the host; the renderer only learns that the call failed.
                UnexpectedError?.Invoke(type, ex);
                error = "internal error";
            }
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteBoolean("ok", writeResult is not null);
            if (writeResult is not null)
            {
                writer.WritePropertyName("result");
                writeResult(writer);
            }
            else
            {
                writer.WriteString("error", error);
            }
            writer.WriteEndObject();
        }
        _post(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static bool TryReadEnvelope(JsonElement root, out int id, out string type, out JsonElement payload)
    {
        id = 0;
        type = "";
        payload = default;
        if (root.ValueKind != JsonValueKind.Object) return false;
        var seen = 0;
        foreach (var prop in root.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "id" when prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out id):
                    seen |= 1;
                    break;
                case "type" when prop.Value.ValueKind == JsonValueKind.String:
                    type = prop.Value.GetString()!;
                    seen |= 2;
                    break;
                case "payload" when prop.Value.ValueKind == JsonValueKind.Object:
                    payload = prop.Value;
                    seen |= 4;
                    break;
                default:
                    return false;
            }
        }
        return seen == 7;
    }
}
