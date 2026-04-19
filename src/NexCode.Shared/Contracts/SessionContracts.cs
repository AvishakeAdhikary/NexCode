using NexCode.Shared.Models;

namespace NexCode.Shared.Contracts;

public sealed record SessionCreateRequest(
    string ProjectPath,
    SessionMode Mode,
    ExecutionMode ExecutionMode,
    PermissionLevel PermissionLevel,
    bool SandboxEnabled);

public sealed record SessionCreateResponse(
    Guid SessionId,
    DateTimeOffset CreatedAt);

public sealed record SessionSendMessageRequest(
    Guid SessionId,
    string Content);

public sealed record SessionCancelRequest(
    Guid SessionId,
    string? Reason);

public sealed record PlanConfirmRequest(Guid PlanId);

public sealed record PlanRejectRequest(Guid PlanId, string? Reason);
