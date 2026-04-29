using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Data.Storage;
using NexCode.Service;
using NexCode.Service.SubAgents;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests.SubAgents;

public sealed class SubAgentManagerTests
{
    [Fact]
    public async Task SpawnAsync_TierGate_RejectsFreeTier()
    {
        var (manager, _, dbPath) = CreateManager();
        try
        {
            var caps = new SubscriptionCapabilitiesPayload(
                CanUseSandbox: false,
                CanUseRemoteExecution: false,
                CanUseCloudExecution: false,
                MaxConcurrentSessions: 1,
                MaxSubAgentsPerSession: 0);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.SpawnAsync(
                    new SubAgentSpawnRequest(Guid.NewGuid(), "{\"mode\":\"helper\"}"),
                    caps,
                    new SubAgentManager.PermissionLevelDescriptor(PermissionLevel.Default, false),
                    CancellationToken.None));

            Assert.Contains("Pro", ex.Message);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task SpawnAsync_PersistsRowAndPublishesAgentSpawned()
    {
        var (manager, hub, dbPath) = CreateManager();
        try
        {
            var caps = SubscriptionTier.Pro.ToCapabilities();
            var response = await manager.SpawnAsync(
                new SubAgentSpawnRequest(Guid.NewGuid(), "{\"mode\":\"helper\"}"),
                caps,
                new SubAgentManager.PermissionLevelDescriptor(PermissionLevel.Default, false),
                CancellationToken.None);

            Assert.NotEqual(Guid.Empty, response.SubAgentId);

            var events = hub.Poll(0).Events;
            Assert.Contains(events, e => e.EventType == ServiceEventTypes.AgentSpawned);

            var listing = await manager.ListAsync(CancellationToken.None);
            Assert.Single(listing.SubAgents);
            Assert.Equal(response.SubAgentId, listing.SubAgents[0].Id);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task KillAsync_MarksRowAsKilledAndPublishesAgentEnded()
    {
        var (manager, hub, dbPath) = CreateManager();
        try
        {
            var caps = SubscriptionTier.Pro.ToCapabilities();
            var spawn = await manager.SpawnAsync(
                new SubAgentSpawnRequest(Guid.NewGuid(), "{\"mode\":\"helper\"}"),
                caps,
                new SubAgentManager.PermissionLevelDescriptor(PermissionLevel.Default, false),
                CancellationToken.None);

            // Drain events from spawn so we can isolate kill events.
            var afterSpawnSeq = hub.Poll(0).LatestSequence;

            await manager.KillAsync(spawn.SubAgentId, CancellationToken.None);

            var endEvents = hub.Poll(afterSpawnSeq).Events;
            Assert.Contains(endEvents, e => e.EventType == ServiceEventTypes.AgentEnded);

            var listing = await manager.ListAsync(CancellationToken.None);
            var row = listing.SubAgents.Single();
            Assert.Equal(SubAgentStatuses.Killed, row.Status);
            Assert.NotNull(row.EndedAt);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    private static (SubAgentManager Manager, ServiceEventHub Hub, string DbPath) CreateManager()
    {
        SqlCipherBootstrapper.EnsureInitialized();
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexcode-subagent-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using (var ctx = new NexCodeDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }
        var hub = new ServiceEventHub();
        var factory = new TestDbContextFactory(options);
        var manager = new SubAgentManager(
            factory,
            hub,
            NullLogger<SubAgentManager>.Instance,
            new NoOpLauncher());
        return (manager, hub, dbPath);
    }

    private static void DeleteDb(string dbPath)
    {
        foreach (var path in new[] { dbPath, $"{dbPath}-shm", $"{dbPath}-wal" })
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

    /// <summary>
    /// Replaces the real CLI subprocess with a long-lived sleep so the lifecycle path is
    /// exercised without depending on dotnet exec. The spawn test asserts AgentSpawned
    /// fires; the kill test asserts AgentEnded fires after KillAsync wins the race.
    /// </summary>
    private sealed class NoOpLauncher : ISubAgentLauncher
    {
        public Process Launch(Guid subAgentId, Guid parentSessionId, string configJson)
        {
            // Use ping with a 30-second timeout to keep the process alive long enough
            // for KillAsync to win the race in the test.
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "ping.exe" : "/bin/sh",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (OperatingSystem.IsWindows())
            {
                psi.ArgumentList.Add("-n");
                psi.ArgumentList.Add("30");
                psi.ArgumentList.Add("127.0.0.1");
            }
            else
            {
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("sleep 30");
            }
            return Process.Start(psi)!;
        }
    }
}

internal static class TierExtensions
{
    public static SubscriptionCapabilitiesPayload ToCapabilities(this SubscriptionTier tier)
    {
        return tier switch
        {
            SubscriptionTier.Pro => new SubscriptionCapabilitiesPayload(true, true, false, 5, 3),
            _ => new SubscriptionCapabilitiesPayload(false, false, false, 1, 0),
        };
    }
}
