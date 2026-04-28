using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Streaming adapter for OpenAI's <c>POST /v1/responses</c> Responses API. Translates
/// <see cref="ProviderTurnRequest"/> into the Responses wire shape, parses the SSE event
/// stream, and yields <see cref="ProviderEvent"/> records.
/// </summary>
public sealed class OpenAIResponsesProvider : IModelProvider
{
    public const string HttpClientName = "openai";
    private const string DefaultBaseUrl = "https://api.openai.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProviderHttpRetryPolicy _retryPolicy;

    public OpenAIResponsesProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new ProviderHttpRetryPolicy())
    {
    }

    public OpenAIResponsesProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _retryPolicy = retryPolicy;
    }

    public string Key => "openai";
    public string DisplayName => "OpenAI";

    public async IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = string.IsNullOrWhiteSpace(request.Configuration.BaseUrl)
            ? DefaultBaseUrl
            : request.Configuration.BaseUrl.TrimEnd('/');
        var endpoint = new Uri(baseUrl + "/v1/responses");
        var bodyJson = BuildRequestBody(request);

        var pendingRetries = new List<ProviderRetryEvent>();
        HttpResponseMessage? response = null;
        ProviderErrorEvent? sendError = null;
        try
        {
            response = await _retryPolicy.SendAsync(
                client,
                () => CreateRequest(endpoint, request.Configuration.ApiKey, bodyJson),
                pendingRetries.Add,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sendError = new ProviderErrorEvent(
                "openai.network",
                "Request was canceled before headers arrived.",
                Recoverable: true);
        }
        catch (HttpRequestException ex)
        {
            sendError = new ProviderErrorEvent("openai.network", ex.Message, Recoverable: true);
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
                "openai.retries_exhausted",
                "OpenAI Responses API returned a transient error after the configured retries.",
                Recoverable: false);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new ProviderErrorEvent(
                    $"openai.http_{(int)response.StatusCode}",
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
        Stream? stream = null;
        ProviderErrorEvent? streamError = null;
        try
        {
            stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            streamError = new ProviderErrorEvent(
                "openai.stream_canceled",
                "Stream was canceled before any payload arrived.",
                Recoverable: true);
        }

        if (streamError is not null || stream is null)
        {
            if (streamError is not null)
            {
                yield return streamError;
            }
            yield break;
        }

        await using var reader = new SseEventReader(stream);

        var toolBuffers = new Dictionary<string, ToolItemBuffer>();
        var finishReason = "stop";
        int? promptTokens = null;
        int? completionTokens = null;
        var faulted = false;
        ProviderErrorEvent? failureEvent = null;

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
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failureEvent = new ProviderErrorEvent("openai.stream_interrupted", "Server-Sent Event stream was interrupted mid-turn.", Recoverable: true);
                    faulted = true;
                    break;
                }
                catch (IOException ex)
                {
                    failureEvent = new ProviderErrorEvent("openai.stream_io", ex.Message, Recoverable: true);
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
                    toEmit = HandleEvent(sse, toolBuffers, ref finishReason, ref promptTokens, ref completionTokens);
                }
                catch (JsonException ex)
                {
                    failureEvent = new ProviderErrorEvent("openai.parse", ex.Message, Recoverable: false);
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

        if (faulted && failureEvent is not null)
        {
            yield return failureEvent;
        }
    }

    private static ProviderEvent? HandleEvent(
        SseEvent sse,
        Dictionary<string, ToolItemBuffer> toolBuffers,
        ref string finishReason,
        ref int? promptTokens,
        ref int? completionTokens)
    {
        using var doc = JsonDocument.Parse(sse.Data);
        var root = doc.RootElement;
        var type = sse.EventName;
        if (string.IsNullOrEmpty(type) && root.TryGetProperty("type", out var typeProp))
        {
            type = typeProp.GetString() ?? string.Empty;
        }

        switch (type)
        {
            case "response.output_text.delta":
                if (root.TryGetProperty("delta", out var deltaEl) && deltaEl.ValueKind == JsonValueKind.String)
                {
                    var text = deltaEl.GetString() ?? string.Empty;
                    return text.Length == 0 ? null : new TextDeltaEvent(text);
                }
                return null;

            case "response.output_item.added":
                if (root.TryGetProperty("item", out var addedItem)
                    && addedItem.TryGetProperty("type", out var addedType)
                    && string.Equals(addedType.GetString(), "function_call", StringComparison.Ordinal))
                {
                    var itemId = addedItem.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    var callId = addedItem.TryGetProperty("call_id", out var callIdEl) ? callIdEl.GetString() ?? itemId : itemId;
                    var name = addedItem.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrEmpty(itemId))
                    {
                        toolBuffers[itemId] = new ToolItemBuffer(callId, name);
                    }
                }
                return null;

            case "response.function_call_arguments.delta":
                if (root.TryGetProperty("item_id", out var deltaItemId)
                    && root.TryGetProperty("delta", out var argsDelta)
                    && argsDelta.ValueKind == JsonValueKind.String)
                {
                    var key = deltaItemId.GetString() ?? string.Empty;
                    if (!toolBuffers.TryGetValue(key, out var buffer))
                    {
                        buffer = new ToolItemBuffer(key, string.Empty);
                        toolBuffers[key] = buffer;
                    }
                    buffer.Arguments.Append(argsDelta.GetString());
                }
                return null;

            case "response.output_item.done":
                if (root.TryGetProperty("item", out var doneItem)
                    && doneItem.TryGetProperty("type", out var doneType)
                    && string.Equals(doneType.GetString(), "function_call", StringComparison.Ordinal))
                {
                    var itemId = doneItem.TryGetProperty("id", out var doneIdEl) ? doneIdEl.GetString() ?? string.Empty : string.Empty;
                    var callId = doneItem.TryGetProperty("call_id", out var doneCallEl) ? doneCallEl.GetString() ?? itemId : itemId;
                    var name = doneItem.TryGetProperty("name", out var doneNameEl) ? doneNameEl.GetString() ?? string.Empty : string.Empty;

                    string args;
                    if (toolBuffers.TryGetValue(itemId, out var buffer))
                    {
                        args = buffer.Arguments.Length == 0 ? "{}" : buffer.Arguments.ToString();
                        if (string.IsNullOrEmpty(name))
                        {
                            name = buffer.Name;
                        }
                        if (string.IsNullOrEmpty(callId))
                        {
                            callId = buffer.CallId;
                        }
                        toolBuffers.Remove(itemId);
                    }
                    else if (doneItem.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                    {
                        args = argsEl.GetString() ?? "{}";
                    }
                    else
                    {
                        args = "{}";
                    }

                    return new ToolUseRequestedEvent(callId, name, args);
                }
                return null;

            case "response.completed":
                if (root.TryGetProperty("response", out var completedResponse))
                {
                    if (completedResponse.TryGetProperty("usage", out var usage))
                    {
                        if (usage.TryGetProperty("input_tokens", out var inEl) && inEl.ValueKind == JsonValueKind.Number)
                        {
                            promptTokens = inEl.GetInt32();
                        }
                        if (usage.TryGetProperty("output_tokens", out var outEl) && outEl.ValueKind == JsonValueKind.Number)
                        {
                            completionTokens = outEl.GetInt32();
                        }
                    }
                    if (completedResponse.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                    {
                        finishReason = statusEl.GetString() ?? finishReason;
                    }
                }
                return new TurnCompletedEvent(finishReason, promptTokens, completionTokens);

            case "response.failed":
            case "response.error":
            case "error":
                var message = "OpenAI error";
                var code = "openai.error";
                if (root.TryGetProperty("error", out var errorObj))
                {
                    if (errorObj.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                    {
                        message = msgEl.GetString() ?? message;
                    }
                    if (errorObj.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String)
                    {
                        code = codeEl.GetString() ?? code;
                    }
                }
                else if (root.TryGetProperty("response", out var failedResponse)
                         && failedResponse.TryGetProperty("error", out var failedErr))
                {
                    if (failedErr.TryGetProperty("message", out var msgEl2) && msgEl2.ValueKind == JsonValueKind.String)
                    {
                        message = msgEl2.GetString() ?? message;
                    }
                    if (failedErr.TryGetProperty("code", out var codeEl2) && codeEl2.ValueKind == JsonValueKind.String)
                    {
                        code = codeEl2.GetString() ?? code;
                    }
                }
                return new ProviderErrorEvent(code, message, Recoverable: false);

            default:
                return null;
        }
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, string apiKey, string bodyJson)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.ModelId);
            writer.WriteNumber("max_output_tokens", request.MaxOutputTokens);
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteBoolean("stream", true);

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                writer.WriteString("instructions", request.SystemPrompt);
            }

            writer.WritePropertyName("input");
            writer.WriteStartArray();
            foreach (var message in request.Conversation)
            {
                WriteInputItems(writer, message);
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
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters");
                    tool.InputSchema.WriteTo(writer);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteInputItems(Utf8JsonWriter writer, ProviderConversationMessage message)
    {
        switch (message.Role)
        {
            case ProviderMessageRole.System:
                // Lifted to top-level "instructions"; if multiple are supplied, drop subsequent ones.
                if (!string.IsNullOrEmpty(message.Content))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "system");
                    writer.WriteString("content", message.Content);
                    writer.WriteEndObject();
                }
                return;

            case ProviderMessageRole.Tool:
                if (message.ToolResults is { Count: > 0 } results)
                {
                    foreach (var result in results)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "function_call_output");
                        writer.WriteString("call_id", result.CallId);
                        writer.WriteString("output", result.ResultJson);
                        writer.WriteEndObject();
                    }
                }
                return;

            case ProviderMessageRole.Assistant:
                if (!string.IsNullOrEmpty(message.Content))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "assistant");
                    writer.WriteString("content", message.Content);
                    writer.WriteEndObject();
                }
                if (message.ToolUses is { Count: > 0 } toolUses)
                {
                    foreach (var use in toolUses)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "function_call");
                        writer.WriteString("call_id", use.CallId);
                        writer.WriteString("name", use.ToolName);
                        writer.WriteString("arguments", string.IsNullOrEmpty(use.ArgumentsJson) ? "{}" : use.ArgumentsJson);
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

    private sealed class ToolItemBuffer(string callId, string name)
    {
        public string CallId { get; } = callId;
        public string Name { get; } = name;
        public StringBuilder Arguments { get; } = new();
    }
}
