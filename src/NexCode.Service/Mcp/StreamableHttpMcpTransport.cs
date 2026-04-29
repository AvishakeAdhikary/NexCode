using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NexCode.Service.Providers;

namespace NexCode.Service.Mcp;

/// <summary>
/// MCP Streamable HTTP transport (spec 2025-11-25). POSTs JSON-RPC requests to
/// <c>{base_url}/mcp</c>. The endpoint may answer with either a single JSON document
/// (when the response can be returned synchronously) or an SSE stream that ends with the
/// matching JSON-RPC response. Out-of-band server-sent notifications are surfaced via
/// <see cref="NotificationReceived"/>.
/// </summary>
public sealed class StreamableHttpMcpTransport : IMcpTransport
{
    public const string HttpClientName = "mcp-streamable-http";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private string? _baseUrl;
    private string? _bearerToken;
    private string? _sessionId;
    private long _idSeed;

    public StreamableHttpMcpTransport(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public event EventHandler<JsonElement>? NotificationReceived;

    public Task ConnectAsync(JsonElement config, CancellationToken cancellationToken)
    {
        var baseUrl = TryGetString(config, "base_url")
            ?? TryGetString(config, "baseUrl")
            ?? TryGetString(config, "url")
            ?? throw new ArgumentException("streamable_http MCP config requires 'base_url'.");
        _baseUrl = baseUrl.TrimEnd('/');
        _bearerToken = TryGetString(config, "bearer_token") ?? TryGetString(config, "bearerToken");
        return Task.CompletedTask;
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var id = Interlocked.Increment(ref _idSeed).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var bodyJson = BuildRequest(id, method, parameters);
        using var request = BuildHttpRequest(bodyJson);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            CaptureSessionHeader(response);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await SafeReadStringAsync(response, cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"MCP HTTP {(int)response.StatusCode}: {errBody ?? response.ReasonPhrase}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                await ReadSseAsync(response, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    DispatchPayload(json);
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetException(ex);
        }

        using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }
    }

    public async Task SendNotificationAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var bodyJson = BuildNotification(method, parameters);
        using var request = BuildHttpRequest(bodyJson);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        CaptureSessionHeader(response);
    }

    public Task DisconnectAsync()
    {
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();
        _sessionId = null;
        _baseUrl = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(DisconnectAsync());
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrEmpty(_baseUrl))
        {
            throw new InvalidOperationException("Streamable HTTP transport not connected.");
        }
    }

    private HttpRequestMessage BuildHttpRequest(string bodyJson)
    {
        var endpoint = new Uri($"{_baseUrl}/mcp");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrEmpty(_bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
        if (!string.IsNullOrEmpty(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }
        return request;
    }

    private void CaptureSessionHeader(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                {
                    _sessionId = v;
                    break;
                }
            }
        }
    }

    private async Task ReadSseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = new SseEventReader(stream);
        await foreach (var sse in reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sse.Data.Length == 0 || string.Equals(sse.Data, "[DONE]", StringComparison.Ordinal))
            {
                continue;
            }
            DispatchPayload(sse.Data);
        }
    }

    private void DispatchPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            DispatchElement(doc.RootElement);
        }
        catch (JsonException)
        {
            // ignore non-JSON SSE keepalives
        }
    }

    private void DispatchElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                DispatchElement(item);
            }
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
        {
            var id = idEl.ValueKind switch
            {
                JsonValueKind.String => idEl.GetString() ?? string.Empty,
                JsonValueKind.Number => idEl.GetRawText(),
                _ => idEl.GetRawText(),
            };
            if (_pending.TryRemove(id, out var tcs))
            {
                if (root.TryGetProperty("error", out var errEl))
                {
                    var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
                    tcs.TrySetException(new InvalidOperationException(msg ?? "MCP error"));
                }
                else if (root.TryGetProperty("result", out var resEl))
                {
                    tcs.TrySetResult(resEl.Clone());
                }
                else
                {
                    tcs.TrySetResult(default);
                }
                return;
            }
        }

        // Treat as notification.
        NotificationReceived?.Invoke(this, root.Clone());
    }

    private static string BuildRequest(string id, string method, JsonElement parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                parameters.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildNotification(string method, JsonElement parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                parameters.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static async Task<string?> SafeReadStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
