using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Shared implementation for providers that speak the OpenAI <c>POST /v1/chat/completions</c>
/// streaming dialect (Groq, OpenRouter, LM Studio, Azure OpenAI, generic custom). Subclasses
/// provide their wire-level identity (key, default URL, auth header) and inherit the SSE
/// parsing + tool-call accumulation logic.
/// </summary>
public abstract class OpenAiCompatibleChatProvider : IModelProvider
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ProviderHttpRetryPolicy RetryPolicy;

    protected OpenAiCompatibleChatProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new ProviderHttpRetryPolicy())
    {
    }

    protected OpenAiCompatibleChatProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy retryPolicy)
    {
        HttpClientFactory = httpClientFactory;
        RetryPolicy = retryPolicy;
    }

    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    protected abstract string HttpClientNameValue { get; }
    protected abstract string DefaultBaseUrl { get; }
    protected virtual string ChatPath => "/v1/chat/completions";

    /// <summary>Override to control how the API key is attached to the outgoing request.</summary>
    protected virtual void ApplyAuth(HttpRequestMessage request, ProviderConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }
    }

    /// <summary>Override to mutate the request body builder before serialization.</summary>
    protected virtual void OnBuildRequestBody(Utf8JsonWriter writer, ProviderTurnRequest request)
    {
    }

    public async IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = HttpClientFactory.CreateClient(HttpClientNameValue);
        var baseUrl = string.IsNullOrWhiteSpace(request.Configuration.BaseUrl)
            ? DefaultBaseUrl
            : request.Configuration.BaseUrl.TrimEnd('/');
        var endpoint = new Uri(baseUrl + ChatPath);
        var bodyJson = BuildBody(request);

        var pendingRetries = new List<ProviderRetryEvent>();
        HttpResponseMessage? response = null;
        ProviderErrorEvent? sendError = null;
        try
        {
            response = await RetryPolicy.SendAsync(
                client,
                () => CreateRequest(endpoint, request.Configuration, bodyJson),
                pendingRetries.Add,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sendError = new ProviderErrorEvent($"{Key}.network", "Request was canceled before headers arrived.", true);
        }
        catch (HttpRequestException ex)
        {
            sendError = new ProviderErrorEvent($"{Key}.network", ex.Message, true);
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
            yield return new ProviderErrorEvent(
                $"{Key}.retries_exhausted",
                $"{DisplayName} returned a transient error after the configured retries.",
                Recoverable: false);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new ProviderErrorEvent(
                    $"{Key}.http_{(int)response.StatusCode}",
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

    private async IAsyncEnumerable<ProviderEvent> ReadStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var reader = new SseEventReader(stream);

        var toolBuffers = new Dictionary<int, ToolBuffer>();
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
                    failure = new ProviderErrorEvent($"{Key}.stream_io", ex.Message, true);
                    faulted = true;
                    break;
                }
                if (!moved)
                {
                    break;
                }

                var sse = enumerator.Current;
                if (sse.Data.Length == 0 || string.Equals(sse.Data, "[DONE]", StringComparison.Ordinal))
                {
                    continue;
                }

                ProviderEvent? toEmit = null;
                try
                {
                    toEmit = HandleStreamEvent(sse, toolBuffers, ref finishReason, ref promptTokens, ref completionTokens);
                }
                catch (JsonException ex)
                {
                    failure = new ProviderErrorEvent($"{Key}.parse", ex.Message, false);
                    faulted = true;
                    break;
                }

                if (toEmit is not null)
                {
                    yield return toEmit;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (faulted && failure is not null)
        {
            yield return failure;
            yield break;
        }

        // Emit any pending tool-use blocks plus a turn-completion at end of stream.
        foreach (var buffer in toolBuffers.Values)
        {
            yield return new ToolUseRequestedEvent(
                buffer.CallId ?? string.Empty,
                buffer.Name ?? string.Empty,
                buffer.Arguments.Length == 0 ? "{}" : buffer.Arguments.ToString());
        }
        toolBuffers.Clear();
        yield return new TurnCompletedEvent(finishReason, promptTokens, completionTokens);
    }

    private static ProviderEvent? HandleStreamEvent(
        SseEvent sse,
        Dictionary<int, ToolBuffer> toolBuffers,
        ref string finishReason,
        ref int? promptTokens,
        ref int? completionTokens)
    {
        using var doc = JsonDocument.Parse(sse.Data);
        var root = doc.RootElement;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
            {
                promptTokens = pt.GetInt32();
            }
            if (usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
            {
                completionTokens = ct.GetInt32();
            }
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                finishReason = fr.GetString() ?? finishReason;
            }
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    return new TextDeltaEvent(text);
                }
            }
            if (delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    var index = call.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
                        ? idxEl.GetInt32()
                        : 0;
                    if (!toolBuffers.TryGetValue(index, out var buffer))
                    {
                        buffer = new ToolBuffer();
                        toolBuffers[index] = buffer;
                    }
                    if (call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        buffer.CallId ??= idEl.GetString();
                    }
                    if (call.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                    {
                        if (fn.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                        {
                            buffer.Name ??= nm.GetString();
                        }
                        if (fn.TryGetProperty("arguments", out var ar) && ar.ValueKind == JsonValueKind.String)
                        {
                            buffer.Arguments.Append(ar.GetString());
                        }
                    }
                }
            }
        }
        return null;
    }

    protected virtual HttpRequestMessage CreateRequest(Uri endpoint, ProviderConfiguration configuration, string bodyJson)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        ApplyAuth(message, configuration);
        return message;
    }

    private string BuildBody(ProviderTurnRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.ModelId);
            writer.WriteNumber("max_tokens", request.MaxOutputTokens);
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteBoolean("stream", true);

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", request.SystemPrompt);
                writer.WriteEndObject();
            }
            foreach (var message in request.Conversation)
            {
                WriteMessage(writer, message);
            }
            writer.WriteEndArray();

            if (request.Tools.Count > 0)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                foreach (var tool in request.Tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WritePropertyName("function");
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters");
                    tool.InputSchema.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            OnBuildRequestBody(writer, request);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, ProviderConversationMessage message)
    {
        switch (message.Role)
        {
            case ProviderMessageRole.System:
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", message.Content ?? string.Empty);
                writer.WriteEndObject();
                return;
            case ProviderMessageRole.Assistant:
                writer.WriteStartObject();
                writer.WriteString("role", "assistant");
                writer.WriteString("content", message.Content ?? string.Empty);
                if (message.ToolUses is { Count: > 0 } uses)
                {
                    writer.WritePropertyName("tool_calls");
                    writer.WriteStartArray();
                    foreach (var use in uses)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", use.CallId);
                        writer.WriteString("type", "function");
                        writer.WritePropertyName("function");
                        writer.WriteStartObject();
                        writer.WriteString("name", use.ToolName);
                        writer.WriteString("arguments", string.IsNullOrWhiteSpace(use.ArgumentsJson) ? "{}" : use.ArgumentsJson);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
                return;
            case ProviderMessageRole.Tool:
                if (message.ToolResults is { Count: > 0 } results)
                {
                    foreach (var result in results)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("role", "tool");
                        writer.WriteString("tool_call_id", result.CallId);
                        writer.WriteString("content", result.ResultJson);
                        writer.WriteEndObject();
                    }
                }
                return;
            case ProviderMessageRole.User:
            default:
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", message.Content ?? string.Empty);
                writer.WriteEndObject();
                return;
        }
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

    private sealed class ToolBuffer
    {
        public string? CallId;
        public string? Name;
        public StringBuilder Arguments { get; } = new();
    }
}
