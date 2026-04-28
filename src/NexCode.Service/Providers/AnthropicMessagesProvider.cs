using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Streaming adapter for Anthropic's <c>POST /v1/messages</c> Messages API. Translates
/// <see cref="ProviderTurnRequest"/> into Anthropic's wire shape, parses the SSE response,
/// and yields <see cref="ProviderEvent"/> records.
/// </summary>
public sealed class AnthropicMessagesProvider : IModelProvider
{
    public const string HttpClientName = "anthropic";
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string AnthropicVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProviderHttpRetryPolicy _retryPolicy;

    public AnthropicMessagesProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new ProviderHttpRetryPolicy())
    {
    }

    public AnthropicMessagesProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy retryPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _retryPolicy = retryPolicy;
    }

    public string Key => "anthropic";
    public string DisplayName => "Anthropic Claude";

    public async IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = string.IsNullOrWhiteSpace(request.Configuration.BaseUrl)
            ? DefaultBaseUrl
            : request.Configuration.BaseUrl.TrimEnd('/');
        var endpoint = new Uri(baseUrl + "/v1/messages");
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
                "anthropic.network",
                "Request was canceled before headers arrived.",
                Recoverable: true);
        }
        catch (HttpRequestException ex)
        {
            sendError = new ProviderErrorEvent("anthropic.network", ex.Message, Recoverable: true);
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
                "anthropic.retries_exhausted",
                "Anthropic Messages API returned a transient error after the configured retries.",
                Recoverable: false);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new ProviderErrorEvent(
                    $"anthropic.http_{(int)response.StatusCode}",
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
                "anthropic.stream_canceled",
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

        var toolBuffers = new Dictionary<int, ToolBlockBuffer>();
        var finishReason = "end_turn";
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
                    failureEvent = new ProviderErrorEvent("anthropic.stream_interrupted", "Server-Sent Event stream was interrupted mid-turn.", Recoverable: true);
                    faulted = true;
                    break;
                }
                catch (IOException ex)
                {
                    failureEvent = new ProviderErrorEvent("anthropic.stream_io", ex.Message, Recoverable: true);
                    faulted = true;
                    break;
                }

                if (!moved)
                {
                    break;
                }

                var sse = enumerator.Current;
                if (sse.Data.Length == 0)
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
                    failureEvent = new ProviderErrorEvent("anthropic.parse", ex.Message, Recoverable: false);
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
            yield break;
        }
    }

    private static ProviderEvent? HandleEvent(
        SseEvent sse,
        Dictionary<int, ToolBlockBuffer> toolBuffers,
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
            case "content_block_start":
                if (root.TryGetProperty("index", out var startIdx)
                    && root.TryGetProperty("content_block", out var block)
                    && block.TryGetProperty("type", out var blockType)
                    && string.Equals(blockType.GetString(), "tool_use", StringComparison.Ordinal))
                {
                    var idx = startIdx.GetInt32();
                    var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    var name = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    toolBuffers[idx] = new ToolBlockBuffer(id, name);
                }
                return null;

            case "content_block_delta":
                if (!root.TryGetProperty("delta", out var delta))
                {
                    return null;
                }
                var deltaType = delta.TryGetProperty("type", out var dtEl) ? dtEl.GetString() : null;
                if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
                {
                    var text = delta.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty;
                    return text.Length == 0 ? null : new TextDeltaEvent(text);
                }
                if (string.Equals(deltaType, "input_json_delta", StringComparison.Ordinal))
                {
                    if (root.TryGetProperty("index", out var deltaIdx)
                        && delta.TryGetProperty("partial_json", out var partialEl)
                        && toolBuffers.TryGetValue(deltaIdx.GetInt32(), out var buffer))
                    {
                        buffer.Arguments.Append(partialEl.GetString());
                    }
                }
                return null;

            case "content_block_stop":
                if (root.TryGetProperty("index", out var stopIdx)
                    && toolBuffers.TryGetValue(stopIdx.GetInt32(), out var stopBuffer))
                {
                    toolBuffers.Remove(stopIdx.GetInt32());
                    var args = stopBuffer.Arguments.Length == 0 ? "{}" : stopBuffer.Arguments.ToString();
                    return new ToolUseRequestedEvent(stopBuffer.Id, stopBuffer.Name, args);
                }
                return null;

            case "message_delta":
                if (root.TryGetProperty("delta", out var msgDelta)
                    && msgDelta.TryGetProperty("stop_reason", out var stopReason)
                    && stopReason.ValueKind == JsonValueKind.String)
                {
                    finishReason = stopReason.GetString() ?? finishReason;
                }
                if (root.TryGetProperty("usage", out var usage))
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
                return null;

            case "message_start":
                if (root.TryGetProperty("message", out var startMsg)
                    && startMsg.TryGetProperty("usage", out var startUsage)
                    && startUsage.TryGetProperty("input_tokens", out var startIn)
                    && startIn.ValueKind == JsonValueKind.Number)
                {
                    promptTokens = startIn.GetInt32();
                }
                return null;

            case "message_stop":
                return new TurnCompletedEvent(finishReason, promptTokens, completionTokens);

            case "error":
                var errMessage = root.TryGetProperty("error", out var errObj) && errObj.TryGetProperty("message", out var errMsg)
                    ? errMsg.GetString() ?? "Anthropic error"
                    : "Anthropic error";
                var errCode = root.TryGetProperty("error", out var errObj2) && errObj2.TryGetProperty("type", out var errType)
                    ? errType.GetString() ?? "anthropic.error"
                    : "anthropic.error";
                return new ProviderErrorEvent(errCode, errMessage, Recoverable: false);

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
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", AnthropicVersion);
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
            writer.WriteNumber("max_tokens", request.MaxOutputTokens);
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteBoolean("stream", true);

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                writer.WriteString("system", request.SystemPrompt);
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            WriteMessages(writer, request.Conversation);
            writer.WriteEndArray();

            if (request.Tools.Count > 0)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                foreach (var tool in request.Tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("input_schema");
                    tool.InputSchema.WriteTo(writer);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessages(Utf8JsonWriter writer, IReadOnlyList<ProviderConversationMessage> messages)
    {
        // Group consecutive Tool-role messages onto the previous user-role frame as per Anthropic's API.
        var pending = new List<ProviderToolResultRecord>();
        ProviderConversationMessage? buffered = null;

        void Flush()
        {
            if (buffered is null && pending.Count == 0)
            {
                return;
            }

            if (buffered is null)
            {
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                foreach (var result in pending)
                {
                    WriteToolResult(writer, result);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                pending.Clear();
                return;
            }

            WriteMessage(writer, buffered, pending);
            buffered = null;
            pending.Clear();
        }

        foreach (var message in messages)
        {
            if (message.Role == ProviderMessageRole.System)
            {
                continue; // already lifted to top-level "system"
            }

            if (message.Role == ProviderMessageRole.Tool)
            {
                if (message.ToolResults is { Count: > 0 } results)
                {
                    pending.AddRange(results);
                }
                continue;
            }

            Flush();
            buffered = message;
        }

        Flush();
    }

    private static void WriteMessage(
        Utf8JsonWriter writer,
        ProviderConversationMessage message,
        List<ProviderToolResultRecord> trailingToolResults)
    {
        writer.WriteStartObject();
        writer.WriteString("role", message.Role switch
        {
            ProviderMessageRole.Assistant => "assistant",
            _ => "user",
        });

        writer.WritePropertyName("content");
        writer.WriteStartArray();

        if (!string.IsNullOrEmpty(message.Content))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", message.Content);
            writer.WriteEndObject();
        }

        if (message.Role == ProviderMessageRole.Assistant && message.ToolUses is { Count: > 0 } toolUses)
        {
            foreach (var use in toolUses)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "tool_use");
                writer.WriteString("id", use.CallId);
                writer.WriteString("name", use.ToolName);
                writer.WritePropertyName("input");
                if (string.IsNullOrWhiteSpace(use.ArgumentsJson))
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
                else
                {
                    using var argsDoc = JsonDocument.Parse(use.ArgumentsJson);
                    argsDoc.RootElement.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
        }

        if (message.Role == ProviderMessageRole.User && trailingToolResults.Count > 0)
        {
            foreach (var result in trailingToolResults)
            {
                WriteToolResult(writer, result);
            }
            trailingToolResults.Clear();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteToolResult(Utf8JsonWriter writer, ProviderToolResultRecord result)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "tool_result");
        writer.WriteString("tool_use_id", result.CallId);
        if (result.IsError)
        {
            writer.WriteBoolean("is_error", true);
        }
        writer.WriteString("content", result.ResultJson);
        writer.WriteEndObject();
    }

    private sealed class ToolBlockBuffer(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public StringBuilder Arguments { get; } = new();
    }
}
