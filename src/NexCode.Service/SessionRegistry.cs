using System.Collections.Concurrent;
using NexCode.Shared.Contracts;

namespace NexCode.Service;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<Guid, SessionRuntimeState> _sessions = new();

    public int Count => _sessions.Count;

    public SessionCreateResponse Create(SessionCreateRequest request)
    {
        var state = new SessionRuntimeState(Guid.NewGuid(), request, DateTimeOffset.UtcNow);
        _sessions[state.SessionId] = state;
        return new SessionCreateResponse(state.SessionId, state.CreatedAt);
    }

    public bool Cancel(Guid sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }

    public bool TryGet(Guid sessionId, out SessionRuntimeState? state)
    {
        return _sessions.TryGetValue(sessionId, out state);
    }
}
