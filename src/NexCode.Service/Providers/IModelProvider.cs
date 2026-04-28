using System.Collections.Generic;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Spec §5.3 / §24 streaming model provider abstraction. Each adapter (Anthropic, OpenAI,
/// Gemini, Ollama, etc.) implements this interface and yields <see cref="ProviderEvent"/>
/// records as the model produces tokens, requests tool calls, and finishes turns.
/// </summary>
public interface IModelProvider
{
    /// <summary>Stable identifier matching <see cref="ProviderConfiguration.ProviderKey"/>.</summary>
    string Key { get; }

    /// <summary>Human-readable display name (e.g. "Anthropic Claude").</summary>
    string DisplayName { get; }

    /// <summary>
    /// Runs a single turn against the model. The implementation must:
    ///   - honor <paramref name="cancellationToken"/> at every awaited boundary
    ///   - respect HTTP <c>Retry-After</c> headers on 429 / 503 responses up to a small bound
    ///   - emit <see cref="ToolUseRequestedEvent"/> when the model wants a tool to run, then
    ///     wait for the caller to feed back tool results in the conversation list on the next
    ///     <see cref="StreamTurnAsync"/> invocation
    ///   - never throw mid-stream for transport errors that have a structured equivalent;
    ///     emit <see cref="ProviderErrorEvent"/> instead
    /// </summary>
    IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Bundle of inputs handed to a model provider for a single streaming turn.</summary>
public sealed record ProviderTurnRequest(
    ProviderConfiguration Configuration,
    string ModelId,
    string SystemPrompt,
    IReadOnlyList<ProviderConversationMessage> Conversation,
    IReadOnlyList<ProviderToolDescriptor> Tools,
    int MaxOutputTokens,
    double Temperature);

/// <summary>
/// Provider configuration loaded from the encrypted <c>Providers</c> table.
/// <see cref="ApiKey"/> is in plaintext only after decryption inside the helper process.
/// </summary>
public sealed record ProviderConfiguration(
    string ProviderKey,
    string DisplayName,
    string BaseUrl,
    string ApiKey,
    string DefaultModelId);

/// <summary>One message in a turn-aware conversation.</summary>
public sealed record ProviderConversationMessage(
    ProviderMessageRole Role,
    string Content,
    IReadOnlyList<ProviderToolUseRecord>? ToolUses = null,
    IReadOnlyList<ProviderToolResultRecord>? ToolResults = null);

public enum ProviderMessageRole { User, Assistant, System, Tool }

/// <summary>Records that the assistant requested a tool call in a prior turn.</summary>
public sealed record ProviderToolUseRecord(
    string CallId,
    string ToolName,
    string ArgumentsJson);

/// <summary>Records the result of a tool call from a prior turn.</summary>
public sealed record ProviderToolResultRecord(
    string CallId,
    string ResultJson,
    bool IsError);

/// <summary>
/// Spec §5.4 tool description as the LLM sees it. Schema must be a JSON Schema fragment
/// that providers translate into their native tool/function/responses-tool format.
/// </summary>
public sealed record ProviderToolDescriptor(
    string Name,
    string Description,
    JsonElement InputSchema);

/// <summary>Discriminated union of streaming events emitted by a provider.</summary>
public abstract record ProviderEvent;

public sealed record TextDeltaEvent(string Delta) : ProviderEvent;

public sealed record ToolUseRequestedEvent(
    string CallId,
    string ToolName,
    string ArgumentsJson) : ProviderEvent;

public sealed record TurnCompletedEvent(
    string FinishReason,
    int? PromptTokens,
    int? CompletionTokens) : ProviderEvent;

public sealed record ProviderErrorEvent(
    string Code,
    string Message,
    bool Recoverable) : ProviderEvent;

public sealed record ProviderRetryEvent(
    int Attempt,
    string Reason,
    TimeSpan Delay) : ProviderEvent;
