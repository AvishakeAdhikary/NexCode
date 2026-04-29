using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Streaming adapter for Google's Generative Language API
/// <c>POST /v1beta/models/{model}:streamGenerateContent</c>. Auth uses the <c>?key=</c>
/// query parameter when an API key is supplied. Function calling is mapped from
/// <see cref="ProviderToolDescriptor"/> to Gemini's <c>tools[].function_declarations</c>.
/// </summary>
public sealed class GeminiProvider : IModelProvider
{
    public const string HttpClientName = "gemini";
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProviderHttpRetryPolicy _retryPolicy;

    public GeminiProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new ProviderHttpRetryPolicy())
    {
    }

    public GeminiProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _retryPolicy = retryPolicy;
    }

    public string Key => "gemini";
    public string DisplayName => "Google Gemini";

    public async IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = string.IsNullOrWhiteSpace(request.Configuration.BaseUrl)
            ? DefaultBaseUrl
            : request.Configuration.BaseUrl.TrimEnd('/');
        var key = request.Configuration.ApiKey;
        var endpoint = new Uri(
            $"{baseUrl}/v1beta/models/{Uri.EscapeDataString(request.ModelId)}:streamGenerateContent?alt=sse"
            + (string.IsNullOrEmpty(key) ? string.Empty : $"&key={Uri.EscapeDataString(key)}"));
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
            sendError = new ProviderErrorEvent("gemini.network", "Request canceled before headers arrived.", true);
        }
        catch (HttpRequestException ex)
        {
            sendError = new ProviderErrorEvent("gemini.network", ex.Message, true);
        }

        foreach (var retry in pendingRetries) yield return retry;
        if (sendError is not null) { yield return sendError; yield break; }
        if (response is null)
        {
            yield return new ProviderErrorEvent("gemini.retries_exhausted", "Gemini returned a transient error after retries.", false);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new ProviderErrorEvent(
                    $"gemini.http_{(int)response.StatusCode}",
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
        await using var reader = new SseEventReader(stream);

        var finishReason = "stop";
        int? promptTokens = null;
        int? completionTokens = null;
        ProviderErrorEvent? failure = null;
        var faulted = false;

        var enumerator = reader.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failure = new ProviderErrorEvent("gemini.stream_io", ex.Message, true);
                    faulted = true;
                    break;
                }
                if (!moved) break;

                var sse = enumerator.Current;
                if (sse.Data.Length == 0) continue;

                ProviderEvent? toEmit = null;
                ProviderEvent? toolEvent = null;
                try
                {
                    using var doc = JsonDocument.Parse(sse.Data);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var candidate in candidates.EnumerateArray())
                        {
                            if (candidate.TryGetProperty("finishReason", out var fr) && fr.ValueKind == JsonValueKind.String)
                            {
                                finishReason = fr.GetString() ?? finishReason;
                            }
                            if (!candidate.TryGetProperty("content", out var content)) continue;
                            if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;

                            foreach (var part in parts.EnumerateArray())
                            {
                                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                                {
                                    var s = text.GetString();
                                    if (!string.IsNullOrEmpty(s))
                                    {
                                        toEmit = new TextDeltaEvent(s);
                                    }
                                }
                                if (part.TryGetProperty("functionCall", out var fc) && fc.ValueKind == JsonValueKind.Object)
                                {
                                    var name = fc.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                                    var argsJson = fc.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                                    var callId = $"gemini_{Guid.NewGuid():N}";
                                    toolEvent = new ToolUseRequestedEvent(callId, name, argsJson);
                                }
                            }
                        }
                    }

                    if (root.TryGetProperty("usageMetadata", out var usage))
                    {
                        if (usage.TryGetProperty("promptTokenCount", out var pe) && pe.ValueKind == JsonValueKind.Number)
                        {
                            promptTokens = pe.GetInt32();
                        }
                        if (usage.TryGetProperty("candidatesTokenCount", out var ce) && ce.ValueKind == JsonValueKind.Number)
                        {
                            completionTokens = ce.GetInt32();
                        }
                    }

                    if (root.TryGetProperty("error", out var errEl))
                    {
                        var msg = errEl.TryGetProperty("message", out var em) ? em.GetString() : "Gemini error";
                        failure = new ProviderErrorEvent("gemini.error", msg ?? "Gemini error", false);
                        faulted = true;
                        break;
                    }
                }
                catch (JsonException ex)
                {
                    failure = new ProviderErrorEvent("gemini.parse", ex.Message, false);
                    faulted = true;
                    break;
                }

                if (toEmit is not null) yield return toEmit;
                if (toolEvent is not null) yield return toolEvent;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (faulted && failure is not null) { yield return failure; yield break; }
        yield return new TurnCompletedEvent(finishReason, promptTokens, completionTokens);
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, string bodyJson)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
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
        => GeminiRequestEncoder.BuildRequestBody(request);
}
