using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexCode.Data.Extensions;
using NexCode.Data.Storage;
using NexCode.Service;
using NexCode.Service.Telemetry;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.Telemetry;

public sealed class TelemetryQueueTests
{
    [Fact]
    public async Task Enqueue_Then_GetSize_ReportsCountAndBytes()
    {
        var dbPath = NewDb();
        try
        {
            await using var provider = BuildProvider(dbPath);
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();
            var queue = provider.GetRequiredService<TelemetryQueue>();

            await queue.EnqueueAsync(MakeEvent("session_started", new { id = "abc" }));
            await queue.EnqueueAsync(MakeEvent("tool_called", new { name = "read_file" }));

            var size = await queue.GetSizeAsync();
            Assert.Equal(2, size.QueuedEvents);
            Assert.True(size.QueuedBytes > 0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Drain_ReturnsOldestFirst()
    {
        var dbPath = NewDb();
        try
        {
            await using var provider = BuildProvider(dbPath);
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();
            var queue = provider.GetRequiredService<TelemetryQueue>();

            await queue.EnqueueAsync(MakeEvent("event_1", new { i = 1 }));
            await Task.Delay(20);
            await queue.EnqueueAsync(MakeEvent("event_2", new { i = 2 }));
            await Task.Delay(20);
            await queue.EnqueueAsync(MakeEvent("event_3", new { i = 3 }));

            var drained = await queue.DrainAsync(2);

            Assert.Equal(2, drained.Count);
            Assert.Equal("event_1", drained[0].Event.Kind);
            Assert.Equal("event_2", drained[1].Event.Kind);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Ack_RemovesOnlyAckedRows()
    {
        var dbPath = NewDb();
        try
        {
            await using var provider = BuildProvider(dbPath);
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();
            var queue = provider.GetRequiredService<TelemetryQueue>();

            await queue.EnqueueAsync(MakeEvent("event_a", new { }));
            await queue.EnqueueAsync(MakeEvent("event_b", new { }));
            await queue.EnqueueAsync(MakeEvent("event_c", new { }));

            var drained = await queue.DrainAsync(10);
            await queue.AckAsync(drained.Take(2).Select(d => d.Id));

            var size = await queue.GetSizeAsync();
            Assert.Equal(1, size.QueuedEvents);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Clear_RemovesEverything()
    {
        var dbPath = NewDb();
        try
        {
            await using var provider = BuildProvider(dbPath);
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();
            var queue = provider.GetRequiredService<TelemetryQueue>();

            await queue.EnqueueAsync(MakeEvent("event_a", new { }));
            await queue.EnqueueAsync(MakeEvent("event_b", new { }));

            var cleared = await queue.ClearAsync();

            Assert.Equal(2, cleared);
            var size = await queue.GetSizeAsync();
            Assert.Equal(0, size.QueuedEvents);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Drain_EmptyQueue_ReturnsEmpty()
    {
        var dbPath = NewDb();
        try
        {
            await using var provider = BuildProvider(dbPath);
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();
            var queue = provider.GetRequiredService<TelemetryQueue>();

            var drained = await queue.DrainAsync(10);
            Assert.Empty(drained);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static TelemetryEvent MakeEvent(string kind, object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload);
        return new TelemetryEvent(kind, element, DateTimeOffset.UtcNow);
    }

    private static ServiceProvider BuildProvider(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDatabaseKeyProvider>(new FixedKeyDatabaseKeyProvider(MakeKey(0xDD)));
        services.AddNexCodeData(dbPath);
        services.AddSingleton<ServiceEventHub>();
        services.AddSingleton<TelemetryQueue>();
        return services.BuildServiceProvider();
    }

    private static byte[] MakeKey(byte fill)
    {
        var key = new byte[IDatabaseKeyProvider.KeyLengthBytes];
        Array.Fill(key, fill);
        return key;
    }

    private static string NewDb() =>
        Path.Combine(Path.GetTempPath(), $"nexcode-tq-{Guid.NewGuid():N}.db");

    private static void Cleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, $"{path}-shm", $"{path}-wal" })
        {
            if (!File.Exists(f)) { continue; }
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}
