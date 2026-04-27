using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Data.Repositories;

public sealed class SessionRepository(IDbContextFactory<NexCodeDbContext> dbContextFactory) : ISessionRepository
{
    public async Task PersistSessionCreatedAsync(
        Guid sessionId,
        SessionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedPath = Path.GetFullPath(request.ProjectPath);
        var now = DateTimeOffset.UtcNow;
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(item => item.DirectoryPath == normalizedPath, cancellationToken);

        if (project is null)
        {
            project = new ProjectEntity
            {
                Id = Guid.NewGuid(),
                DirectoryPath = normalizedPath,
                DisplayName = ResolveProjectDisplayName(normalizedPath),
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Projects.Add(project);
        }
        else
        {
            project.UpdatedAt = now;
        }

        var session = await dbContext.Sessions
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);

        if (session is null)
        {
            session = new SessionEntity
            {
                Id = sessionId,
                ProjectId = project.Id,
                PermissionLevel = request.PermissionLevel,
                ExecutionMode = request.ExecutionMode,
                SandboxEnabled = request.SandboxEnabled,
                Title = $"{request.Mode} session",
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Sessions.Add(session);
        }
        else
        {
            session.ProjectId = project.Id;
            session.PermissionLevel = request.PermissionLevel;
            session.ExecutionMode = request.ExecutionMode;
            session.SandboxEnabled = request.SandboxEnabled;
            session.Title = $"{request.Mode} session";
            session.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TurnShellPersistenceResult> CreateTurnShellAsync(
        Guid sessionId,
        string userContent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var userMessageId = Guid.NewGuid();
        var assistantMessageId = Guid.NewGuid();
        dbContext.Messages.Add(new MessageEntity
        {
            Id = userMessageId,
            SessionId = sessionId,
            Role = "user",
            Content = userContent,
            CreatedAt = now
        });
        dbContext.Messages.Add(new MessageEntity
        {
            Id = assistantMessageId,
            SessionId = sessionId,
            Role = "assistant",
            Content = string.Empty,
            CreatedAt = now.AddMilliseconds(1)
        });

        var session = await dbContext.Sessions
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is not null)
        {
            session.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new TurnShellPersistenceResult(userMessageId, assistantMessageId, now);
    }

    public async Task UpdateMessageContentAsync(
        Guid messageId,
        string content,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var message = await dbContext.Messages
            .SingleAsync(item => item.Id == messageId, cancellationToken);

        message.Content = content;
        message.CompletionTokens = EstimateTokenCount(content);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> CreateCheckpointAsync(
        Guid sessionId,
        Guid assistantMessageId,
        string? gitCommitHash,
        string diffSummary,
        string[] filesChanged,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var checkpointId = Guid.NewGuid();
        dbContext.Checkpoints.Add(new CheckpointEntity
        {
            Id = checkpointId,
            SessionId = sessionId,
            MessageId = assistantMessageId,
            GitCommitHash = gitCommitHash,
            DiffSnapshot = JsonSerializer.Serialize(
                new CheckpointDiffSnapshot(diffSummary, filesChanged),
                JsonSerialization.Options),
            CreatedAt = DateTimeOffset.UtcNow
        });

        var message = await dbContext.Messages
            .SingleAsync(item => item.Id == assistantMessageId, cancellationToken);
        message.CheckpointId = checkpointId;

        var session = await dbContext.Sessions
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is not null)
        {
            session.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return checkpointId;
    }

    private static string ResolveProjectDisplayName(string normalizedPath)
    {
        var trimmedPath = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(directoryName)
            ? trimmedPath
            : directoryName;
    }

    private static int EstimateTokenCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        return Math.Max(1, content.Length / 4);
    }

    private sealed record CheckpointDiffSnapshot(
        string Summary,
        string[] ChangedPaths);
}
