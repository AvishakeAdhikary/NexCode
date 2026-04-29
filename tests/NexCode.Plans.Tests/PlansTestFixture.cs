using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Storage;
using NexCode.Service;
using NexCode.Service.Plans;

namespace NexCode.Plans.Tests;

/// <summary>
/// Shared test fixture: spins up an unencrypted in-memory SQLite connection (kept open for
/// the lifetime of the test) and wires <see cref="PlanManager"/>, <see cref="TodoManager"/>,
/// and <see cref="ClarifyEngine"/> against a real <see cref="NexCodeDbContext"/>.
/// </summary>
internal sealed class PlansTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<NexCodeDbContext> _options;

    public PlansTestFixture()
    {
        SqlCipherBootstrapper.EnsureInitialized();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var db = new NexCodeDbContext(_options))
        {
            db.Database.EnsureCreated();
        }

        Factory = new TestDbContextFactory(_options);
        EventHub = new ServiceEventHub();
        PlanManager = new PlanManager(Factory, EventHub);
        TodoManager = new TodoManager(Factory, EventHub, PlanManager);
        ClarifyEngine = new ClarifyEngine(Factory, EventHub);
    }

    public TestDbContextFactory Factory { get; }
    public ServiceEventHub EventHub { get; }
    public PlanManager PlanManager { get; }
    public TodoManager TodoManager { get; }
    public ClarifyEngine ClarifyEngine { get; }

    public NexCodeDbContext CreateContext() => new(_options);

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}

internal sealed class TestDbContextFactory(DbContextOptions<NexCodeDbContext> options)
    : IDbContextFactory<NexCodeDbContext>
{
    public NexCodeDbContext CreateDbContext() => new(options);

    public Task<NexCodeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<NexCodeDbContext>(new NexCodeDbContext(options));
}
