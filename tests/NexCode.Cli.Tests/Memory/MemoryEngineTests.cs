using Microsoft.EntityFrameworkCore;
using NexCode.Data.Storage;
using NexCode.Service;
using NexCode.Service.Memory;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests.Memory;

public sealed class MemoryEngineTests
{
    [Fact]
    public async Task Write_Then_Read_RoundTripsValue()
    {
        var (engine, factory, dbPath) = CreateEngine();
        try
        {
            await engine.WriteAsync("favorite_lang", "C#", MemoryScope.Global, null, null, null);
            var read = await engine.ReadAsync("favorite_lang", MemoryScope.Global, null, null);

            Assert.NotNull(read);
            Assert.Equal("favorite_lang", read!.Key);
            Assert.Equal("C#", read.Value);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesMemory()
    {
        var (engine, factory, dbPath) = CreateEngine();
        try
        {
            var summary = await engine.WriteAsync("ephemeral", "abc", MemoryScope.Global, null, null, null);
            var deleted = await engine.DeleteAsync(summary.Id);
            Assert.True(deleted);

            var read = await engine.ReadAsync("ephemeral", MemoryScope.Global, null, null);
            Assert.Null(read);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task GlobalCap_AutoEvicts_OldestLastAccessed()
    {
        var (engine, factory, dbPath) = CreateEngine();
        try
        {
            // Write 60 keys (cap is 50). Oldest by LastAccessedAt should be evicted.
            for (var i = 0; i < 60; i++)
            {
                await engine.WriteAsync($"key{i:D2}", $"value-{i}", MemoryScope.Global, null, null, null);
                // Small spacing so LastAccessedAt is monotonic across writes.
                await Task.Delay(2);
            }

            var listed = await engine.ListAsync(MemoryScope.Global, null, null);
            Assert.Equal(MemoryEngine.FreeGlobalCap, listed.Length);

            // The most recent write must survive; the very first must have been evicted.
            Assert.Contains(listed, m => m.Key == "key59");
            Assert.DoesNotContain(listed, m => m.Key == "key00");
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task RelevantAsync_ReturnsTopN_BySimilarity()
    {
        var (engine, factory, dbPath) = CreateEngine();
        try
        {
            await engine.WriteAsync("k1", "We use SQLCipher for encrypted SQLite databases.", MemoryScope.Global, null, null, null);
            await engine.WriteAsync("k2", "The user prefers tabs over spaces.", MemoryScope.Global, null, null, null);
            await engine.WriteAsync("k3", "Rust ownership system enforces borrow checker rules.", MemoryScope.Global, null, null, null);
            await engine.WriteAsync("k4", "SQLite database file lives in LocalAppData\\NexCode.", MemoryScope.Global, null, null, null);

            var hits = await engine.RelevantAsync("encrypted sqlite database", null, topN: 2);

            Assert.Equal(2, hits.Length);
            // k1 and k4 both mention sqlite/database; ordering is by cosine similarity but both
            // should rank above the unrelated rust/tabs entries.
            var keys = hits.Select(h => h.Key).ToHashSet();
            Assert.Contains("k1", keys);
            Assert.Contains("k4", keys);
            Assert.DoesNotContain("k2", keys);
            Assert.DoesNotContain("k3", keys);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task SessionScope_StaysInProcess_AndIsNotPersisted()
    {
        var (engine, factory, dbPath) = CreateEngine();
        try
        {
            var sessionId = Guid.NewGuid();
            await engine.WriteAsync("scratch", "remember me", MemoryScope.Session, null, sessionId, null);

            var read = await engine.ReadAsync("scratch", MemoryScope.Session, null, sessionId);
            Assert.NotNull(read);
            Assert.Equal("remember me", read!.Value);

            // Session-scoped writes should NOT have been persisted.
            await using var dbContext = await factory.CreateDbContextAsync();
            var count = await dbContext.Memories.CountAsync();
            Assert.Equal(0, count);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    private static (MemoryEngine Engine, IDbContextFactory<NexCodeDbContext> Factory, string DbPath) CreateEngine()
    {
        SqlCipherBootstrapper.EnsureInitialized();

        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-memory-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using (var dbContext = new NexCodeDbContext(options))
        {
            dbContext.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(options);
        var engine = new MemoryEngine(factory, new SessionMemoryStore(), new ServiceEventHub());
        return (engine, factory, dbPath);
    }

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
        {
            if (!File.Exists(path)) continue;
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<NexCodeDbContext> options) : IDbContextFactory<NexCodeDbContext>
    {
        public NexCodeDbContext CreateDbContext() => new(options);
        public Task<NexCodeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
