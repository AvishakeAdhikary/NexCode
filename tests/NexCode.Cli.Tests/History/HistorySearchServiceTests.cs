using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexCode.Data.Entities;
using NexCode.Data.Extensions;
using NexCode.Data.Storage;
using NexCode.Service.History;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.History;

public sealed class HistorySearchServiceTests
{
    [Fact]
    public async Task Search_ReturnsHits_ForInsertedMessages()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-fts-{Guid.NewGuid():N}.db");
        try
        {
            var services = BuildServices(dbPath);
            await using var provider = services.BuildServiceProvider();

            var initializer = provider.GetRequiredService<NexCodeDatabaseInitializer>();
            await initializer.EnsureCreatedAsync();

            var dbFactory = provider.GetRequiredService<IDbContextFactory<NexCodeDbContext>>();
            var sessionId = Guid.NewGuid();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Sessions.Add(new SessionEntity { Id = sessionId });
                db.Messages.Add(new MessageEntity
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    Role = "user",
                    Content = "How do we add full text search to nexcode messages?"
                });
                db.Messages.Add(new MessageEntity
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    Role = "assistant",
                    Content = "Use SQLite FTS5 over the Messages table."
                });
                await db.SaveChangesAsync();
            }

            var service = provider.GetRequiredService<HistorySearchService>();
            await service.InitializeAsync();

            var hits = await service.SearchAsync(
                new HistorySearchRequest(
                    Query: "fts5",
                    ProjectFilter: null,
                    DateFromIso: null,
                    DateToIso: null,
                    ModeFilter: null,
                    ProviderFilter: null,
                    Limit: 10));

            Assert.NotEmpty(hits.Results);
            Assert.Contains(hits.Results, hit => hit.SessionId == sessionId);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-fts-{Guid.NewGuid():N}.db");
        try
        {
            var services = BuildServices(dbPath);
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();

            var service = provider.GetRequiredService<HistorySearchService>();
            await service.InitializeAsync();

            var hits = await service.SearchAsync(
                new HistorySearchRequest("", null, null, null, null, null, 10));

            Assert.Empty(hits.Results);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task ListAsync_IncludesMessageCount()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-fts-{Guid.NewGuid():N}.db");
        try
        {
            var services = BuildServices(dbPath);
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();

            var dbFactory = provider.GetRequiredService<IDbContextFactory<NexCodeDbContext>>();
            var sessionId = Guid.NewGuid();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Sessions.Add(new SessionEntity { Id = sessionId, Title = "session 1" });
                for (var i = 0; i < 4; i++)
                {
                    db.Messages.Add(new MessageEntity
                    {
                        Id = Guid.NewGuid(),
                        SessionId = sessionId,
                        Role = "user",
                        Content = $"msg {i}"
                    });
                }
                await db.SaveChangesAsync();
            }

            var service = provider.GetRequiredService<HistorySearchService>();
            var list = await service.ListAsync(new HistoryListRequest(50, null));

            var summary = Assert.Single(list.Sessions, s => s.SessionId == sessionId);
            Assert.Equal("session 1", summary.Title);
            Assert.Equal(4, summary.MessageCount);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesSessionAndMessages()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-fts-{Guid.NewGuid():N}.db");
        try
        {
            var services = BuildServices(dbPath);
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<NexCodeDatabaseInitializer>().EnsureCreatedAsync();

            var dbFactory = provider.GetRequiredService<IDbContextFactory<NexCodeDbContext>>();
            var sessionId = Guid.NewGuid();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Sessions.Add(new SessionEntity { Id = sessionId });
                db.Messages.Add(new MessageEntity
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    Role = "user",
                    Content = "to be deleted"
                });
                await db.SaveChangesAsync();
            }

            var service = provider.GetRequiredService<HistorySearchService>();
            var ok = await service.DeleteAsync(sessionId);

            Assert.True(ok);
            await using var verify = await dbFactory.CreateDbContextAsync();
            Assert.False(await verify.Sessions.AnyAsync(s => s.Id == sessionId));
            Assert.False(await verify.Messages.AnyAsync(m => m.SessionId == sessionId));
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    private static IServiceCollection BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDatabaseKeyProvider>(new FixedKeyDatabaseKeyProvider(MakeKey(0xCC)));
        services.AddNexCodeData(dbPath);
        services.AddSingleton<HistorySearchService>();
        return services;
    }

    private static byte[] MakeKey(byte fill)
    {
        var key = new byte[IDatabaseKeyProvider.KeyLengthBytes];
        Array.Fill(key, fill);
        return key;
    }

    private static void CleanupDatabase(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, $"{path}-shm", $"{path}-wal" })
        {
            if (!File.Exists(f)) { continue; }
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}
