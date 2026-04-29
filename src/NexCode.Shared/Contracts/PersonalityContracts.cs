using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Response payload for <c>personality.list</c>.</summary>
public sealed record PersonalityListResponse(
    [property: JsonPropertyName("personalities")] PersonalitySummary[] Personalities);

/// <summary>
/// Spec §8.2 personality descriptor. Personalities layer a tone-and-verbosity prompt
/// fragment on top of the active mode's system prompt. They can be scoped to a project
/// via <see cref="ProjectId"/> when <see cref="Scope"/> is "project".
/// </summary>
public sealed record PersonalitySummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("system_prompt_fragment")] string SystemPromptFragment,
    [property: JsonPropertyName("tone")] string Tone,
    [property: JsonPropertyName("verbosity")] string Verbosity,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("project_id")] Guid? ProjectId,
    [property: JsonPropertyName("is_default")] bool IsDefault);

public sealed record PersonalityUpsertRequest(
    [property: JsonPropertyName("id")] Guid? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("system_prompt_fragment")] string SystemPromptFragment,
    [property: JsonPropertyName("tone")] string Tone,
    [property: JsonPropertyName("verbosity")] string Verbosity,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("project_id")] Guid? ProjectId,
    [property: JsonPropertyName("is_default")] bool IsDefault);

public sealed record PersonalityDeleteRequest(
    [property: JsonPropertyName("id")] Guid Id);
