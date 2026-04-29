using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Response payload for <c>mode.list</c>: every mode (built-in + custom) the helper knows about.</summary>
public sealed record ModeListResponse(
    [property: JsonPropertyName("modes")] ModeSummary[] Modes);

/// <summary>
/// Spec §7.1 mode descriptor: name, system prompt, optional UI affordances, built-in flag,
/// and the whitelist of tools the agent loop is allowed to expose while this mode is active.
/// </summary>
public sealed record ModeSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("system_prompt")] string SystemPrompt,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("accent_color")] string? AccentColor,
    [property: JsonPropertyName("is_built_in")] bool IsBuiltIn,
    [property: JsonPropertyName("allowed_tools")] string[] AllowedTools);

/// <summary>
/// Request body for <c>mode.upsert</c>. <see cref="Id"/> is null for new modes; non-null Id replaces
/// the existing custom mode. Built-in modes can have their non-identity fields rewritten but are
/// flagged via <see cref="IsBuiltIn"/> so the GUI can disable destructive editors.
/// </summary>
public sealed record ModeUpsertRequest(
    [property: JsonPropertyName("id")] Guid? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("system_prompt")] string SystemPrompt,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("accent_color")] string? AccentColor,
    [property: JsonPropertyName("allowed_tools")] string[]? AllowedTools);

public sealed record ModeDeleteRequest(
    [property: JsonPropertyName("id")] Guid Id);
