using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Sandbox;

/// <summary>
/// Spec §14 facade over <see cref="WindowsJobObject"/>. Tracks one Job Object per sandboxed
/// session id so any process spawned by <c>execute_command</c> (or future tools) can be
/// joined to it. When the session ends, calling <see cref="DisposeForSession"/> closes the
/// kernel handle, which — combined with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> — kills
/// every attached process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SandboxService(ILogger<SandboxService> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, WindowsJobObject> _jobs = new();

    public Task<WindowsJobObject> CreateForSessionAsync(
        SessionRuntimeState session,
        WindowsJobObject.JobLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        var job = _jobs.GetOrAdd(session.SessionId, _ =>
        {
            var created = new WindowsJobObject(limits ?? WindowsJobObject.JobLimits.Default);
            logger.LogInformation("Created sandbox job object for session {SessionId}.", session.SessionId);
            return created;
        });

        return Task.FromResult(job);
    }

    public bool TryGet(Guid sessionId, out WindowsJobObject? job)
    {
        var found = _jobs.TryGetValue(sessionId, out var existing);
        job = found ? existing : null;
        return found;
    }

    public void DisposeForSession(Guid sessionId)
    {
        if (_jobs.TryRemove(sessionId, out var job))
        {
            try
            {
                job.Terminate();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Sandbox terminate threw for session {SessionId}; disposing anyway.", sessionId);
            }
            finally
            {
                job.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var key in _jobs.Keys.ToArray())
        {
            DisposeForSession(key);
        }

        return ValueTask.CompletedTask;
    }
}
