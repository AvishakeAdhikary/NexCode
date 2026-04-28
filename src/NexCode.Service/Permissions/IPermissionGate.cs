using NexCode.Service.Tools;

namespace NexCode.Service.Permissions;

/// <summary>
/// Spec §26 permission gate. Sits between the tool registry and tool execution. Emits a
/// <c>permission_request</c> service event when a tool requires confirmation, blocks until
/// the GUI/CLI responds with <c>permission.respond</c>, and applies persistent decisions.
/// </summary>
public interface IPermissionGate
{
    /// <summary>
    /// Asks the gate whether the given tool may run for this session right now.
    /// Returns the user's decision (or auto-allow / auto-deny) and is responsible for
    /// emitting the <c>permission_request</c> event when needed.
    /// </summary>
    Task<PermissionDecision> RequestAsync(
        Guid sessionId,
        ITool tool,
        string callId,
        string argumentsPreview,
        PermissionMode currentMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a pending permission request when the GUI/CLI responds with
    /// <c>permission.respond</c>.
    /// </summary>
    void Respond(Guid sessionId, string callId, PermissionResponse response);
}

public enum PermissionMode { Default, Full }

public enum PermissionResponse
{
    AllowOnce,
    AllowForSession,
    DenyOnce,
    DenyAlways
}

public sealed record PermissionDecision(
    bool Allowed,
    PermissionResponse Origin,
    string? DenyReason);
