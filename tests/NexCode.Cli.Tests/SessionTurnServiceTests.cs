using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Data.Repositories;
using NexCode.Data.Storage;
using NexCode.Service;
using NexCode.Service.Providers;
using NexCode.Service.Tools;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests;

public sealed class SessionTurnServiceTests
{
    [Fact]
    public async Task ProcessTurnAsync_PersistsAssistantMessageAndCheckpoint_AndPublishesStreamEvents()
    {
        SqlCipherBootstrapper.EnsureInitialized();

        var databasePath = Path.Combine(Path.GetTempPath(), $"nexcode-turn-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            using (var dbContext = new NexCodeDbContext(options))
            {
                dbContext.Database.EnsureCreated();
            }

            var repository = new SessionRepository(new TestDbContextFactory(options));
            var eventHub = new ServiceEventHub();
            var responseProvider = new WorkspaceAwareSessionResponseProvider(new SessionToolExecutor());
            var turnService = new SessionTurnService(
                NullLogger<SessionTurnService>.Instance,
                eventHub,
                repository,
                responseProvider);

            var sessionId = Guid.NewGuid();
            var request = new SessionCreateRequest(
                ProjectPath: "C:\\Projects\\NexCode",
                Mode: SessionMode.Code,
                ExecutionMode: ExecutionMode.Local,
                PermissionLevel: PermissionLevel.Default,
                SandboxEnabled: false);
            await repository.PersistSessionCreatedAsync(sessionId, request);

            var turnShell = await repository.CreateTurnShellAsync(sessionId, "Inspect the workspace and summarize the next step.");
            var session = new SessionRuntimeState(sessionId, request, DateTimeOffset.UtcNow);

            await turnService.ProcessTurnAsync(
                session,
                turnShell.AssistantMessageId,
                "Inspect the workspace and summarize the next step.");

            var events = eventHub.Poll(null).Events;
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.SessionStart);
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.Status);
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.ToolCall);
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.ToolResult);
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.Token);
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.Checkpoint);
            Assert.Contains(events, item => item.EventType == ServiceEventTypes.SessionEnd);

            using var verificationContext = new NexCodeDbContext(options);
            var messages = verificationContext.Messages
                .Where(item => item.SessionId == sessionId)
                .AsEnumerable()
                .OrderBy(item => item.CreatedAt)
                .ToArray();
            var checkpoint = verificationContext.Checkpoints
                .Single(item => item.SessionId == sessionId);

            Assert.Equal(2, messages.Length);
            Assert.Equal("user", messages[0].Role);
            Assert.Equal("assistant", messages[1].Role);
            Assert.False(string.IsNullOrWhiteSpace(messages[1].Content));
            Assert.Equal(checkpoint.Id, messages[1].CheckpointId);
            Assert.Contains("provider abstraction", messages[1].Content);
            Assert.Contains("Checkpoint created after the first provider-backed turn", checkpoint.DiffSnapshot);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     $"{databasePath}-shm",
                     $"{databasePath}-wal"
                 })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // SQLite file handles can linger briefly after test completion; cleanup is best-effort only.
            }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<NexCodeDbContext> options) : IDbContextFactory<NexCodeDbContext>
    {
        public NexCodeDbContext CreateDbContext()
        {
            return new NexCodeDbContext(options);
        }

        public Task<NexCodeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
