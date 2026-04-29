using Microsoft.EntityFrameworkCore;
using NexCode.Data.Storage;
using NexCode.Service.Personalities;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.Personalities;

public sealed class PersonalitiesServiceTests
{
    [Fact]
    public async Task EnsureBuiltInsAsync_SeedsAllFive_Personalities()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            await service.EnsureBuiltInsAsync();
            var response = await service.ListAsync();

            var names = response.Personalities.Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Concise", "Default", "Detailed", "Mentor", "Senior Dev" }, names);
            Assert.Single(response.Personalities, p => p.IsDefault);
            Assert.Equal("Default", response.Personalities.Single(p => p.IsDefault).Name);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAsync_CreatesCustomPersonality()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            var summary = await service.UpsertAsync(new PersonalityUpsertRequest(
                Id: null,
                Name: "PirateMode",
                Description: "Yarr",
                SystemPromptFragment: "speak like a pirate",
                Tone: "playful",
                Verbosity: "medium",
                Scope: "global",
                ProjectId: null,
                IsDefault: false));

            Assert.NotEqual(Guid.Empty, summary.Id);
            Assert.Equal("PirateMode", summary.Name);
            Assert.Equal("global", summary.Scope);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAsync_TogglesDefault_AndClearsOtherDefaults()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            await service.EnsureBuiltInsAsync();

            var newDefault = await service.UpsertAsync(new PersonalityUpsertRequest(
                Id: null,
                Name: "MyPick",
                Description: "x",
                SystemPromptFragment: "x",
                Tone: "neutral",
                Verbosity: "medium",
                Scope: "global",
                ProjectId: null,
                IsDefault: true));

            var response = await service.ListAsync();
            var defaults = response.Personalities.Where(p => p.IsDefault).ToArray();
            Assert.Single(defaults);
            Assert.Equal(newDefault.Id, defaults[0].Id);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RefusesToDelete_BuiltInPersonality()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            await service.EnsureBuiltInsAsync();
            var response = await service.ListAsync();
            var defaultPersonality = response.Personalities.Single(p => p.Name == "Default");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(defaultPersonality.Id));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesCustomPersonality()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            var created = await service.UpsertAsync(new PersonalityUpsertRequest(
                Id: null,
                Name: "Tossable",
                Description: "x",
                SystemPromptFragment: "x",
                Tone: "neutral",
                Verbosity: "low",
                Scope: "global",
                ProjectId: null,
                IsDefault: false));

            var ok = await service.DeleteAsync(created.Id);
            Assert.True(ok);

            var response = await service.ListAsync();
            Assert.DoesNotContain(response.Personalities, p => p.Id == created.Id);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    private static (PersonalitiesService Service, IDbContextFactory<NexCodeDbContext> Factory, string DbPath) CreateService()
    {
        SqlCipherBootstrapper.EnsureInitialized();

        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-personalities-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using (var dbContext = new NexCodeDbContext(options))
        {
            dbContext.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(options);
        return (new PersonalitiesService(factory), factory, dbPath);
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
