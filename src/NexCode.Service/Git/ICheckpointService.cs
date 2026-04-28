namespace NexCode.Service.Git;

/// <summary>
/// Spec §13 checkpoint service. At end of every turn, commits the current working tree to
/// a checkpoint branch (<c>nexcode/checkpoint/&lt;session-id&gt;/&lt;turn&gt;</c>) and returns the
/// commit hash + diff summary + list of files changed. Supports revert + diff retrieval.
/// </summary>
public interface ICheckpointService
{
    /// <summary>
    /// Commit current uncommitted changes (if any) under the project root onto the session's
    /// checkpoint branch. Returns metadata describing the new checkpoint, or
    /// <see langword="null"/> when the project root is not a git repository.
    /// </summary>
    Task<CheckpointResult?> CreateAsync(
        string projectRoot,
        Guid sessionId,
        int turnNumber,
        CancellationToken cancellationToken);

    /// <summary>Returns a unified diff between two refs in <paramref name="projectRoot"/>.</summary>
    Task<string> GetDiffAsync(
        string projectRoot,
        string fromRef,
        string toRef,
        CancellationToken cancellationToken);

    /// <summary>Returns short status (porcelain v1) for the working tree.</summary>
    Task<string> GetStatusAsync(string projectRoot, CancellationToken cancellationToken);

    /// <summary>Reverts the working tree to a prior checkpoint commit (hard reset).</summary>
    Task<bool> RevertAsync(
        string projectRoot,
        string checkpointCommitHash,
        CancellationToken cancellationToken);
}

public sealed record CheckpointResult(
    string CommitHash,
    string DiffSummary,
    string UnifiedDiff,
    IReadOnlyList<string> FilesChanged);
