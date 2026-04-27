namespace NexCode.Shared.Contracts;

public sealed record SessionStartEventPayload(
    Guid SessionId,
    string Mode,
    string Personality,
    string ProjectPath,
    bool SandboxEnabled);

public sealed record SessionEndEventPayload(
    Guid SessionId,
    string Reason);

public sealed record StatusEventPayload(
    Guid SessionId,
    string Message,
    string Level);

public sealed record TokenEventPayload(
    Guid SessionId,
    string Content);

public sealed record ToolCallEventPayload(
    Guid SessionId,
    string ToolName,
    string ArgumentsJson,
    string CallId);

public sealed record ToolResultEventPayload(
    Guid SessionId,
    string CallId,
    string ResultJson,
    bool IsError);

public sealed record CheckpointEventPayload(
    Guid SessionId,
    Guid CheckpointId,
    string? GitCommitHash,
    string DiffSummary,
    string[] FilesChanged);
