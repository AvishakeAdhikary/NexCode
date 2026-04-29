using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Spec §19 / AD-0009 sub-agent IPC contracts. Snake-case wire shape.</summary>
public sealed record SubAgentSpawnRequest(
    [property: JsonPropertyName("parent_session_id")] Guid ParentSessionId,
    [property: JsonPropertyName("config_json")] string ConfigJson);

public sealed record SubAgentSpawnResponse(
    [property: JsonPropertyName("subagent_id")] Guid SubAgentId);

public sealed record SubAgentKillRequest(
    [property: JsonPropertyName("subagent_id")] Guid SubAgentId);

public sealed record SubAgentKillResponse(
    [property: JsonPropertyName("subagent_id")] Guid SubAgentId,
    [property: JsonPropertyName("killed")] bool Killed);

public sealed record SubAgentListResponse(
    [property: JsonPropertyName("subagents")] SubAgentSummary[] SubAgents);

public sealed record SubAgentSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("parent_session_id")] Guid ParentSessionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("config_json")] string ConfigJson,
    [property: JsonPropertyName("spawned_at")] DateTimeOffset SpawnedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset? EndedAt);

public sealed record AgentSpawnedEventPayload(
    [property: JsonPropertyName("subagent_id")] Guid SubAgentId,
    [property: JsonPropertyName("parent_session_id")] Guid ParentSessionId,
    [property: JsonPropertyName("config_json")] string ConfigJson);

public sealed record AgentEndedEventPayload(
    [property: JsonPropertyName("subagent_id")] Guid SubAgentId,
    [property: JsonPropertyName("parent_session_id")] Guid ParentSessionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("exit_code")] int? ExitCode,
    [property: JsonPropertyName("error")] string? Error);

public static class SubAgentStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Killed = "killed";
}
