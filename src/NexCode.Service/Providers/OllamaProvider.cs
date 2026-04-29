using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Streaming adapter for the local Ollama server's <c>POST /api/chat</c> endpoint, which
/// emits JSON-Lines (one JSON object per <c>\n</c> separated line). Default base URL is
/// <c>http://localhost:11434</c>.
/// </summary>
public sealed class OllamaProvider : IModelProvider
{
    public const string HttpClientName = "ollama";
    private const string DefaultBaseUrl = "http://localhost:11434";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProviderHttpRetryPolicy _retryPolicy;

    public OllamaProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new ProviderHttpRetryPolicy())
    {
    }

    public OllamaProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _retryPolicy = retryPolicy;
    }

    public string Key => "ollama";
    public string DisplayName => "Ollama (local)";

    public async IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = string.IsNullOrWhiteSpace(request.Configuration.BaseUrl)
            ? DefaultBaseUrl
            : request.Configuration.BaseUrl.TrimEnd('/');
        var endpoint = new Uri(baseUrl + "/api/chat");
        var bodyJson = BuildRequestBody(request);

        var pendingRetries = new List<ProviderRetryEvent>();
        HttpResponseMessage? response = null;
        ProviderErrorEvent? sendError = null;
        try
        {
            response = await _retryPolicy.SendAsync(
                client,
                () => CreateRequest(endpoint, bodyJson),
                pendingRetries.Add,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sendError = new ProviderErrorEvent("ollama.network", "Request canceled before headers arrived.", true);
        }
        catch (HttpRequestException ex)
        {
            sendError = new ProviderErrorEvent("ollama.network", ex.Message, true);
        }

        foreach (var retry in pendingRetries)
        {
            yield return retry;
        }
        if (sendError is not null)
        {
            yield return sendError;
            yield break;
        }
        if (response is null)
        {
            yield return new ProviderErrorEvent("ollama.retries_exhausted", "Ollama returned a transient error after retries.", false);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new ProviderErrorEvent(
                    $"ollama.http_{(int)response.StatusCode}",
                    string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "HTTP error" : body,
                    Recoverable: false);
                yield break;
            }

            await foreach (var evt in ReadStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }

    private static async IAsyncEnumerable<ProviderEvent> ReadStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);

        var finishReason = "stop";
        int? promptTokens = null;
        int? completionTokens = null;
        ProviderErrorEvent? failure = null;
        var faulted = false;
        var emittedToolCalls = new HashSet<string>(StringComparer.Ordinal);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = new ProviderErrorEvent("ollama.stream_io", ex.Message, true);
                faulted = true;
                break;
            }
            if (line is null)
            {
                break;
            }
            if (line.Length == 0)
            {
                continue;
            }

            ProviderEvent? toEmit = null;
            ProviderEvent? toolEvent = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                {
                    failure = new ProviderErrorEvent("ollama.error", errEl.GetString() ?? "Ollama error", false);
                    faulted = true;
                    break;
                }
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
                {
                    if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            toEmit = new TextDeltaEvent(text);
                        }
                    }
                    if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var call in calls.EnumerateArray())
                        {
                            if (call.ValueKind != JsonValueKind.Object) continue;
                            if (!call.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object) continue;
                            var name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                            var args = fn.TryGetProperty("arguments", out var a)
                                ? (a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : a.GetRawText())
                                : "{}";
                            var callId = $"ollama_{Guid.NewGuid():N}";
                            if (emittedToolCalls.Add(callId))
                            {
                                toolEvent = new ToolUseRequestedEvent(callId, name, args);
                            }
                        }
                    }
                }
                if (root.TryGetProperty("prompt_eval_count", out var pe) && pe.ValueKind == JsonValueKind.Number)
                {
                    promptTokens = pe.GetInt32();
                }
                if (root.TryGetProperty("eval_count", out var ec) && ec.ValueKind == JsonValueKind.Number)
                {
                    completionTokens = ec.GetInt32();
                }
                if (root.TryGetProperty("done_reason", out var dr) && dr.ValueKind == JsonValueKind.String)
                {
                    finishReason = dr.GetString() ?? finishReason;
                }
                if (root.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True)
                {
                    if (toEmit is not null) yield return toEmit;
                    if (toolEvent is not null) yield return toolEvent;
                    yield return new TurnCompletedEvent(finishReason, promptTokens, completionTokens);
                    yield break;
                }
            }
            catch (JsonException ex)
            {
                failure = new ProviderErrorEvent("ollama.parse", ex.Message, false);
                faulted = true;
                break;
            }

            if (toEmit is not null) yield return toEmit;
            if (toolEvent is not null) yield return toolEvent;
        }

        if (faulted && failure is not null)
        {
            yield return failure;
        }
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, string bodyJson)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
        return message;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildRequestBody(ProviderTurnRequest request)
        => OllamaRequestEncoder.BuildRequestBody(request);
}
