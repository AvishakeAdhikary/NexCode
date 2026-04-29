using System.Text.Json.Serialization;
using NexCode.Shared.Models;

namespace NexCode.Shared.Contracts;

/// <summary>Response payload for <c>memory.list</c>.</summary>
public sealed record MemoryListResponse(
    [property: JsonPropertyName("memories")] MemorySummary[] Memories);

/// <summary>
/// Spec §9 memory record. Global memories outlive sessions and projects; project-scoped
/// memories are pinned to a single project; session-scoped memories live only in process
/// per <c>AD-0004</c> and are surfaced from the runtime store rather than the database.
/// </summary>
public sealed record MemorySummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("scope")] MemoryScope Scope,
    [property: JsonPropertyName("project_id")] Guid? ProjectId,
    [property: JsonPropertyName("session_id")] Guid? SessionId,
    [property: JsonPropertyName("tags")] string[] Tags,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("last_accessed_at")] DateTimeOffset LastAccessedAt);

public sealed record MemoryReadRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("scope")] MemoryScope Scope,
    [property: JsonPropertyName("project_id")] Guid? ProjectId,
    [property: JsonPropertyName("session_id")] Guid? SessionId);

public sealed record MemoryWriteRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("scope")] MemoryScope Scope,
    [property: JsonPropertyName("project_id")] Guid? ProjectId,
    [property: JsonPropertyName("session_id")] Guid? SessionId,
    [property: JsonPropertyName("tags")] string[]? Tags);

public sealed record MemoryDeleteRequest(
    [property: JsonPropertyName("id")] Guid Id);

/// <summary>
/// Payload of the <c>memory.updated</c> service event. <see cref="Action"/> is one of
/// <c>created</c>, <c>updated</c>, <c>deleted</c>, or <c>evicted</c>.
/// </summary>
public sealed record MemoryUpdatedEventPayload(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("scope")] MemoryScope Scope,
    [property: JsonPropertyName("action")] string Action);
