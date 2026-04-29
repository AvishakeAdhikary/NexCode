using System.Text.Json;

namespace NexCode.Shared.Contracts;

public static class ServiceEventTypes
{
    public const string AuthRequired = "auth.required";
    public const string AuthSuccess = "auth.success";
    public const string SessionLifecycle = "session.lifecycle";
    public const string SessionStart = "session_start";
    public const string SessionEnd = "session_end";
    public const string Status = "status";
    public const string Token = "token";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Checkpoint = "checkpoint";
    public const string PlanUpdated = "plan.updated";
    public const string TodoUpdated = "todo.updated";
    public const string ClarifyQuestion = "clarify.question";
    public const string PermissionRequest = "permission_request";
    public const string ProviderRetry = "provider.retry";
    public const string ProviderError = "provider.error";

    // Slice 0014+
    public const string MemoryUpdated = "memory.updated";
    public const string McpStatus = "mcp.status";
    public const string McpToolEvent = "mcp.tool_event";
    public const string AgentSpawned = "agent.spawned";
    public const string AgentEnded = "agent.ended";
    public const string LinterDiagnostic = "linter.diagnostic";
    public const string AutomationFired = "automation.fired";
    public const string AutomationCompleted = "automation.completed";
    public const string PluginEvent = "plugin.event";
    public const string TelemetryQueueChanged = "telemetry.queue_changed";
    public const string TerminalOutput = "terminal.output";
    public const string TerminalExit = "terminal.exit";
    public const string FileChanged = "file.changed";
    public const string ConfigUpdated = "config.updated";
    public const string SessionTitleUpdated = "session.title_updated";
    public const string IapUpdated = "service.iap_updated";
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
