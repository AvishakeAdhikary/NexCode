using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NexCode.Service.Tools;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Permissions;

/// <summary>
/// Concrete <see cref="IPermissionGate"/> backing spec §26. Tracks per-session decisions in
/// memory, publishes <c>permission_request</c> events through <see cref="ServiceEventHub"/>,
/// and resumes tool execution when the GUI/CLI calls <see cref="Respond"/>.
/// </summary>
public sealed class PermissionGateService(
    ServiceEventHub serviceEventHub,
    ILogger<PermissionGateService> logger) : IPermissionGate
{
    private readonly ConcurrentDictionary<Guid, SessionPermissionState> _sessions = new();

    public async Task<PermissionDecision> RequestAsync(
        Guid sessionId,
        ITool tool,
        string callId,
        string argumentsPreview,
        PermissionMode currentMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrEmpty(callId);

        cancellationToken.ThrowIfCancellationRequested();

        // Full Access mode auto-allows everything (still recorded as AllowOnce).
        if (currentMode == PermissionMode.Full)
        {
            return new PermissionDecision(true, PermissionResponse.AllowOnce, null);
        }

        // Default-requirement tools never need confirmation.
        if (tool.PermissionRequirement == ToolPermissionRequirement.Default)
        {
            return new PermissionDecision(true, PermissionResponse.AllowOnce, null);
        }

        var state = _sessions.GetOrAdd(sessionId, static _ => new SessionPermissionState());

        if (state.FullAccessGranted)
        {
            return new PermissionDecision(true, PermissionResponse.AllowForSession, null);
        }

        if (state.AllowedTools.Contains(tool.Name))
        {
            return new PermissionDecision(true, PermissionResponse.AllowForSession, null);
        }

        if (state.DeniedTools.Contains(tool.Name))
        {
            return new PermissionDecision(
                false,
                PermissionResponse.DenyAlways,
                $"Tool '{tool.Name}' was previously denied for this session.");
        }

        var tcs = new TaskCompletionSource<PermissionResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!state.PendingRequests.TryAdd(callId, tcs))
        {
            // Duplicate call id — surface as a deny so we don't deadlock.
            logger.LogWarning(
                "Duplicate permission call id {CallId} for session {SessionId}; denying.",
                callId,
                sessionId);
            return new PermissionDecision(
                false,
                PermissionResponse.DenyOnce,
                "Duplicate permission request call id.");
        }

        var levelRequired = tool.PermissionRequirement switch
        {
            ToolPermissionRequirement.Default => "Default",
            ToolPermissionRequirement.Warned => "Warned",
            ToolPermissionRequirement.Full => "Full",
            _ => tool.PermissionRequirement.ToString()
        };

        serviceEventHub.Publish(
            ServiceEventTypes.PermissionRequest,
            new PermissionRequestEventPayload(
                SessionId: sessionId,
                ToolName: tool.Name,
                Description: tool.Description,
                ArgumentsPreview: argumentsPreview,
                LevelRequired: levelRequired,
                CallId: callId));

        try
        {
            await using var registration = cancellationToken.Register(static obj =>
            {
                var (source, token) = ((TaskCompletionSource<PermissionResponse>, CancellationToken))obj!;
                source.TrySetCanceled(token);
            }, (tcs, cancellationToken));

            var response = await tcs.Task.ConfigureAwait(false);

            switch (response)
            {
                case PermissionResponse.AllowForSession:
                    state.AllowedTools.Add(tool.Name);
                    if (tool.PermissionRequirement == ToolPermissionRequirement.Full)
                    {
                        state.FullAccessGranted = true;
                    }
                    return new PermissionDecision(true, response, null);

                case PermissionResponse.AllowOnce:
                    return new PermissionDecision(true, response, null);

                case PermissionResponse.DenyAlways:
                    state.DeniedTools.Add(tool.Name);
                    return new PermissionDecision(
                        false,
                        response,
                        $"Tool '{tool.Name}' denied for this session.");

                case PermissionResponse.DenyOnce:
                default:
                    return new PermissionDecision(
                        false,
                        PermissionResponse.DenyOnce,
                        "User denied this invocation.");
            }
        }
        finally
        {
            state.PendingRequests.TryRemove(callId, out _);
        }
    }

    public void Respond(Guid sessionId, string callId, PermissionResponse response)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            logger.LogDebug(
                "Permission response for unknown session {SessionId} call {CallId} ignored.",
                sessionId,
                callId);
            return;
        }

        if (!state.PendingRequests.TryGetValue(callId, out var tcs))
        {
            logger.LogDebug(
                "Permission response for unknown call id {CallId} on session {SessionId} ignored.",
                callId,
                sessionId);
            return;
        }

        tcs.TrySetResult(response);
    }

    private sealed class SessionPermissionState
    {
        public bool FullAccessGranted;
        public HashSet<string> AllowedTools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DeniedTools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, TaskCompletionSource<PermissionResponse>> PendingRequests { get; }
            = new(StringComparer.Ordinal);
    }
}
