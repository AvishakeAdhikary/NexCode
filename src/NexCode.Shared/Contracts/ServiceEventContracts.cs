using System.Text.Json;

namespace NexCode.Shared.Contracts;

public static class ServiceEventTypes
{
    public const string AuthRequired = "auth.required";
    public const string AuthSuccess = "auth.success";
    public const string SessionLifecycle = "session.lifecycle";
    public const string Token = "token";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Checkpoint = "checkpoint";
    public const string PlanUpdated = "plan.updated";
    public const string TodoUpdated = "todo.updated";
    public const string ClarifyQuestion = "clarify.question";
}

public sealed record ServiceEventsPollRequest(
    long? AfterSequence);

public sealed record ServiceEventsPollResponse(
    long LatestSequence,
    ServiceEventEnvelope[] Events);

public sealed record ServiceEventEnvelope(
    long Sequence,
    string EventType,
    DateTimeOffset Timestamp,
    JsonElement Payload);

public sealed record AuthRequiredEventPayload(
    string Reason,
    bool HasMsalConfiguration,
    bool HasCachedToken);

public sealed record AuthSuccessEventPayload(
    string? UserEmail,
    bool IsSuperUser);

public sealed record SessionLifecycleEventPayload(
    Guid SessionId,
    string State,
    string ProjectPath,
    string Mode,
    string ExecutionMode,
    string? Reason);
