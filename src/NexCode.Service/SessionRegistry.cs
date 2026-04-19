using System.Collections.Concurrent;
using NexCode.Shared.Contracts;

namespace NexCode.Service;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<Guid, SessionCreateRequest> _sessions = new();

    public int Count => _sessions.Count;

    public SessionCreateResponse Create(SessionCreateRequest request)
    {
        var response = new SessionCreateResponse(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _sessions[response.SessionId] = request;
        return response;
    }

    public bool Cancel(Guid sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }
}
