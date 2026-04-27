using NexCode.Shared.Contracts;

namespace NexCode.Data.Repositories;

public interface ISessionRepository
{
    Task PersistSessionCreatedAsync(
        Guid sessionId,
        SessionCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<TurnShellPersistenceResult> CreateTurnShellAsync(
        Guid sessionId,
        string userContent,
        CancellationToken cancellationToken = default);

    Task UpdateMessageContentAsync(
        Guid messageId,
        string content,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateCheckpointAsync(
        Guid sessionId,
        Guid assistantMessageId,
        string? gitCommitHash,
        string diffSummary,
        string[] filesChanged,
        CancellationToken cancellationToken = default);
}

public sealed record TurnShellPersistenceResult(
    Guid UserMessageId,
    Guid AssistantMessageId,
    DateTimeOffset CreatedAt);
