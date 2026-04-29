using Microsoft.EntityFrameworkCore;
using NexCode.Data.Storage;
using NexCode.Service.Modes;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.Modes;

public sealed class ModesServiceTests
{
    [Fact]
    public async Task EnsureBuiltInsAsync_SeedsAllFour_BuiltInModes()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            await service.EnsureBuiltInsAsync();
            var response = await service.ListAsync();

            var builtInNames = response.Modes
                .Where(mode => mode.IsBuiltIn)
                .Select(mode => mode.Name)
                .OrderBy(name => name)
                .ToArray();

            Assert.Equal(new[] { "Ask", "Code", "Debug", "Plan" }, builtInNames);
            Assert.All(response.Modes.Where(mode => mode.IsBuiltIn),
                mode => Assert.NotEmpty(mode.SystemPrompt));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task EnsureBuiltInsAsync_IsIdempotent()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            await service.EnsureBuiltInsAsync();
            await service.EnsureBuiltInsAsync();
            await service.EnsureBuiltInsAsync();

            var response = await service.ListAsync();
            Assert.Equal(4, response.Modes.Count(mode => mode.IsBuiltIn));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RefusesToDelete_BuiltInMode()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            await service.EnsureBuiltInsAsync();
            var response = await service.ListAsync();
            var planMode = response.Modes.Single(m => m.Name == "Plan");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(planMode.Id));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAsync_PersistsCustomMode_AndParsesAllowedTools()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            var summary = await service.UpsertAsync(new ModeUpsertRequest(
                Id: null,
                Name: "Quick",
                SystemPrompt: "Be brief.",
                Icon: "Bolt",
                AccentColor: "#FFFFFF",
                AllowedTools: ["read_file", "list_directory"]));

            Assert.NotEqual(Guid.Empty, summary.Id);
            Assert.False(summary.IsBuiltIn);
            Assert.Equal(["read_file", "list_directory"], summary.AllowedTools);

            var response = await service.ListAsync();
            var stored = response.Modes.Single(m => m.Id == summary.Id);
            Assert.Equal("Quick", stored.Name);
            Assert.Equal(["read_file", "list_directory"], stored.AllowedTools);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesCustomMode()
    {
        var (service, factory, dbPath) = CreateService();
        try
        {
            var created = await service.UpsertAsync(new ModeUpsertRequest(
                Id: null,
                Name: "Drafty",
                SystemPrompt: "be terse",
                Icon: null,
                AccentColor: null,
                AllowedTools: null));

            var ok = await service.DeleteAsync(created.Id);
            Assert.True(ok);

            var afterDelete = await service.ListAsync();
            Assert.DoesNotContain(afterDelete.Modes, m => m.Id == created.Id);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    private static (ModesService Service, IDbContextFactory<NexCodeDbContext> Factory, string DbPath) CreateService()
    {
        SqlCipherBootstrapper.EnsureInitialized();

        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-modes-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using (var dbContext = new NexCodeDbContext(options))
        {
            dbContext.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(options);
        return (new ModesService(factory), factory, dbPath);
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
