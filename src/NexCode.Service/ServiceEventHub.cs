using System.Collections.Concurrent;
using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service;

public sealed class ServiceEventHub
{
    private readonly ConcurrentQueue<ServiceEventEnvelope> _events = new();
    private readonly Lock _sync = new();
    private long _nextSequence;
    private const int MaxEvents = 256;

    public ServiceEventEnvelope Publish<TPayload>(string eventType, TPayload payload)
    {
        var envelope = new ServiceEventEnvelope(
            Sequence: Interlocked.Increment(ref _nextSequence),
            EventType: eventType,
            Timestamp: DateTimeOffset.UtcNow,
            Payload: JsonSerializer.SerializeToElement(payload, JsonSerialization.Options));

        _events.Enqueue(envelope);

        lock (_sync)
        {
            while (_events.Count > MaxEvents && _events.TryDequeue(out _))
            {
            }
        }

        return envelope;
    }

    public ServiceEventsPollResponse Poll(long? afterSequence)
    {
        var threshold = afterSequence ?? 0;
        var events = _events
            .Where(item => item.Sequence > threshold)
            .OrderBy(item => item.Sequence)
            .ToArray();

        return new ServiceEventsPollResponse(
            LatestSequence: Volatile.Read(ref _nextSequence),
            Events: events);
    }
}
