using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Telemetry;

/// <summary>
/// Spec §30 telemetry queue. Persists events into the encrypted <c>TelemetryQueue</c> table
/// and exposes drain / ack semantics so the sender can reliably remove only the rows it
/// successfully transmitted.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TelemetryQueue(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    ServiceEventHub serviceEventHub)
{
    public async Task EnqueueAsync(TelemetryEvent ev, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(ev, JsonSerialization.Options);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.TelemetryQueue.Add(new TelemetryQueueEntity
        {
            Id = Guid.NewGuid(),
            PayloadJson = json,
            CreatedAt = ev.Timestamp == default ? DateTimeOffset.UtcNow : ev.Timestamp
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishQueueChangedAsync(db, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(Guid Id, TelemetryEvent Event)>> DrainAsync(
        int max,
        CancellationToken cancellationToken = default)
    {
        if (max <= 0) { return Array.Empty<(Guid, TelemetryEvent)>(); }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TelemetryQueue
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .Take(max)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<(Guid, TelemetryEvent)>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var ev = JsonSerializer.Deserialize<TelemetryEvent>(row.PayloadJson, JsonSerialization.Options);
                if (ev is not null)
                {
                    results.Add((row.Id, ev));
                }
            }
            catch (JsonException)
            {
                // Drop malformed payloads silently to avoid pinning the queue head.
                results.Add((row.Id, new TelemetryEvent("invalid", default, row.CreatedAt)));
            }
        }

        return results;
    }

    public async Task AckAsync(IEnumerable<Guid> rowIds, CancellationToken cancellationToken = default)
    {
        var ids = rowIds?.ToArray() ?? Array.Empty<Guid>();
        if (ids.Length == 0) { return; }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TelemetryQueue
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        db.TelemetryQueue.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishQueueChangedAsync(db, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TelemetryQueue.ToListAsync(cancellationToken).ConfigureAwait(false);
        db.TelemetryQueue.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishQueueChangedAsync(db, cancellationToken).ConfigureAwait(false);
        return rows.Count;
    }

    public async Task<TelemetryQueueResponse> GetSizeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ComputeSizeAsync(db, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishQueueChangedAsync(NexCodeDbContext db, CancellationToken cancellationToken)
    {
        var size = await ComputeSizeAsync(db, cancellationToken).ConfigureAwait(false);
        serviceEventHub.Publish(
            ServiceEventTypes.TelemetryQueueChanged,
            new TelemetryQueueChangedEventPayload(size.QueuedEvents, size.QueuedBytes));
    }

    private static async Task<TelemetryQueueResponse> ComputeSizeAsync(
        NexCodeDbContext db,
        CancellationToken cancellationToken)
    {
        var count = await db.TelemetryQueue.CountAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await db.TelemetryQueue
            .AsNoTracking()
            .Select(t => (long)t.PayloadJson.Length)
            .SumAsync(cancellationToken)
            .ConfigureAwait(false);
        return new TelemetryQueueResponse(count, bytes);
    }
}
