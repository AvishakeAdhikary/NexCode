namespace NexCode.Shared.Contracts;

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
    string Summary,
    string[] ChangedPaths);
