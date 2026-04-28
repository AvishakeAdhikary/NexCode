namespace NexCode.Shared.Contracts;

/// <summary>
/// Spec §26 permission_request event payload. Emitted by the helper when a tool whose
/// permission requirement is unmet wants to run; the GUI/CLI shows a permission prompt
/// card and replies via <c>permission.respond</c>.
/// </summary>
public sealed record PermissionRequestEventPayload(
    Guid SessionId,
    string ToolName,
    string Description,
    string ArgumentsPreview,
    string LevelRequired,
    string CallId);

public sealed record PermissionRespondRequest(
    Guid SessionId,
    string CallId,
    string Decision); // AllowOnce | AllowForSession | DenyOnce | DenyAlways

public sealed record PermissionRespondResponse(
    Guid SessionId,
    string CallId,
    bool Accepted);

public sealed record ProviderRetryEventPayload(
    Guid SessionId,
    int Attempt,
    string Reason,
    int DelayMs);

public sealed record ProviderErrorEventPayload(
    Guid SessionId,
    string Code,
    string Message,
    bool Recoverable);
