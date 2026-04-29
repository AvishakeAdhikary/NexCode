using System.Collections.Concurrent;

namespace NexCode.Service.Memory;

/// <summary>
/// Per-process, runtime-only store for session-scoped memories per AD-0004. Session memory
/// never touches disk: when the helper process exits, the dictionary is gone with it. This
/// is the canonical storage surface for <see cref="NexCode.Shared.Models.MemoryScope.Session"/>
/// reads/writes; the SQLite table is not used for session scope.
/// </summary>
public sealed class SessionMemoryStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, SessionMemoryEntry>> _entries = new();

    public SessionMemoryEntry Write(Guid sessionId, string key, string value, string[] tags)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var bucket = _entries.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<string, SessionMemoryEntry>(StringComparer.Ordinal));
        var now = DateTimeOffset.UtcNow;
        var entry = new SessionMemoryEntry(
            Id: Guid.NewGuid(),
            Key: key,
            Value: value,
            Tags: tags ?? [],
            CreatedAt: now,
            LastAccessedAt: now);

        bucket[key] = entry;
        return entry;
    }

    public SessionMemoryEntry? Read(Guid sessionId, string key)
    {
        if (!_entries.TryGetValue(sessionId, out var bucket) || !bucket.TryGetValue(key, out var entry))
        {
            return null;
        }

        var refreshed = entry with { LastAccessedAt = DateTimeOffset.UtcNow };
        bucket[key] = refreshed;
        return refreshed;
    }

    public IReadOnlyCollection<SessionMemoryEntry> List(Guid sessionId)
    {
        return _entries.TryGetValue(sessionId, out var bucket)
            ? bucket.Values.ToArray()
            : [];
    }

    public bool Delete(Guid sessionId, string key)
    {
        return _entries.TryGetValue(sessionId, out var bucket) && bucket.TryRemove(key, out _);
    }

    public void Forget(Guid sessionId)
    {
        _entries.TryRemove(sessionId, out _);
    }
}

public sealed record SessionMemoryEntry(
    Guid Id,
    string Key,
    string Value,
    string[] Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessedAt);
