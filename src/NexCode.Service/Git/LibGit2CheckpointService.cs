using System.Globalization;
using System.Text;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Git;

/// <summary>
/// LibGit2Sharp-backed <see cref="ICheckpointService"/> implementing spec §13. Each turn
/// captures an isolated commit on <c>nexcode/checkpoint/{sessionId}/{turn}</c> so the
/// user's working branch is never disturbed, while still allowing fast revert.
/// </summary>
public sealed class LibGit2CheckpointService(ILogger<LibGit2CheckpointService> logger)
    : ICheckpointService
{
    private const string CheckpointAuthorName = "nexcode";
    private const string CheckpointAuthorEmail = "noreply@nexcode.local";

    public Task<CheckpointResult?> CreateAsync(
        string projectRoot,
        Guid sessionId,
        int turnNumber,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);

        return Task.Run<CheckpointResult?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Repository.IsValid(projectRoot))
            {
                return null;
            }

            using var repo = new Repository(projectRoot);
            cancellationToken.ThrowIfCancellationRequested();

            var headTip = repo.Head.Tip;
            var branchName = $"nexcode/checkpoint/{sessionId}/{turnNumber}";

            // Compare HEAD tree against index + working directory to discover dirty files.
            var workingChanges = headTip is null
                ? repo.Diff.Compare<TreeChanges>(
                    null,
                    DiffTargets.Index | DiffTargets.WorkingDirectory)
                : repo.Diff.Compare<TreeChanges>(
                    headTip.Tree,
                    DiffTargets.Index | DiffTargets.WorkingDirectory);

            if (workingChanges.Count == 0)
            {
                // Nothing to capture — return current HEAD pointer with empty diff.
                var headSha = headTip?.Sha ?? string.Empty;
                return new CheckpointResult(
                    CommitHash: headSha,
                    DiffSummary: "No changes since last commit.",
                    UnifiedDiff: string.Empty,
                    FilesChanged: Array.Empty<string>());
            }

            // Stage everything (mirrors `git add -A`).
            Commands.Stage(repo, "*");
            cancellationToken.ThrowIfCancellationRequested();

            var stagedChanges = headTip is null
                ? repo.Diff.Compare<TreeChanges>(null, DiffTargets.Index)
                : repo.Diff.Compare<TreeChanges>(headTip.Tree, DiffTargets.Index);

            var patch = headTip is null
                ? repo.Diff.Compare<Patch>((Tree?)null, DiffTargets.Index)
                : repo.Diff.Compare<Patch>(headTip.Tree, DiffTargets.Index);

            var unifiedDiff = patch.Content ?? string.Empty;
            var filesChanged = stagedChanges
                .Select(change => change.Path)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var diffSummary = BuildDiffSummary(patch, filesChanged);

            // Build a checkpoint commit on a side branch without moving HEAD.
            var signature = new Signature(
                CheckpointAuthorName,
                CheckpointAuthorEmail,
                DateTimeOffset.UtcNow);
            var message = $"[nexcode] checkpoint session={sessionId} turn={turnNumber}";

            // Write the staged tree and craft a commit pointing at HEAD as parent.
            var tree = repo.ObjectDatabase.CreateTree(repo.Index);
            var parents = headTip is null ? Array.Empty<Commit>() : new[] { headTip };
            var checkpointCommit = repo.ObjectDatabase.CreateCommit(
                signature,
                signature,
                message,
                tree,
                parents,
                prettifyMessage: false);

            // Create or update the side branch to point at the new commit; HEAD stays put.
            var existing = repo.Branches[branchName];
            if (existing is null)
            {
                repo.Refs.Add($"refs/heads/{branchName}", checkpointCommit.Id);
            }
            else
            {
                repo.Refs.UpdateTarget(existing.Reference, checkpointCommit.Sha);
            }

            // Reset the index back to HEAD so the user's working branch remains "dirty"
            // exactly as it was before — we only wanted to snapshot, not commit-on-branch.
            if (headTip is not null)
            {
                repo.Reset(ResetMode.Mixed, headTip);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new CheckpointResult(
                CommitHash: checkpointCommit.Sha,
                DiffSummary: diffSummary,
                UnifiedDiff: unifiedDiff,
                FilesChanged: filesChanged);
        }, cancellationToken);
    }

    public Task<string> GetDiffAsync(
        string projectRoot,
        string fromRef,
        string toRef,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Repository.IsValid(projectRoot))
            {
                return string.Empty;
            }

            using var repo = new Repository(projectRoot);
            cancellationToken.ThrowIfCancellationRequested();

            var fromTree = ResolveTreeOrNull(repo, fromRef) ?? repo.Head.Tip?.Tree;

            // Null toRef means "compare against working tree".
            Patch patch;
            if (string.IsNullOrEmpty(toRef))
            {
                patch = repo.Diff.Compare<Patch>(
                    fromTree,
                    DiffTargets.Index | DiffTargets.WorkingDirectory);
            }
            else
            {
                var toTree = ResolveTreeOrNull(repo, toRef);
                patch = repo.Diff.Compare<Patch>(fromTree, toTree);
            }

            return patch.Content ?? string.Empty;
        }, cancellationToken);
    }

    public Task<string> GetStatusAsync(string projectRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Repository.IsValid(projectRoot))
            {
                return string.Empty;
            }

            using var repo = new Repository(projectRoot);
            cancellationToken.ThrowIfCancellationRequested();

            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
                IncludeIgnored = false
            });

            var builder = new StringBuilder();
            foreach (var entry in status)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = FormatPorcelainLine(entry);
                if (line is not null)
                {
                    builder.Append(line).Append('\n');
                }
            }

            return builder.ToString();
        }, cancellationToken);
    }

    public Task<bool> RevertAsync(
        string projectRoot,
        string checkpointCommitHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);
        ArgumentException.ThrowIfNullOrEmpty(checkpointCommitHash);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Repository.IsValid(projectRoot))
            {
                return false;
            }

            using var repo = new Repository(projectRoot);
            cancellationToken.ThrowIfCancellationRequested();

            var commit = repo.Lookup<Commit>(checkpointCommitHash);
            if (commit is null)
            {
                logger.LogWarning(
                    "RevertAsync: commit {Hash} not found in {Root}.",
                    checkpointCommitHash,
                    projectRoot);
                return false;
            }

            repo.Reset(ResetMode.Hard, commit);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }, cancellationToken);
    }

    private static Tree? ResolveTreeOrNull(Repository repo, string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var resolved = repo.Lookup(reference);
        return resolved switch
        {
            Commit commit => commit.Tree,
            Tree tree => tree,
            TagAnnotation tagAnnotation when tagAnnotation.Target is Commit tagCommit => tagCommit.Tree,
            _ => null
        };
    }

    private static string BuildDiffSummary(Patch patch, IReadOnlyList<string> filesChanged)
    {
        if (filesChanged.Count == 0)
        {
            return "No changes.";
        }

        var fileFragments = filesChanged
            .Select(path =>
            {
                var stats = patch[path];
                if (stats is null)
                {
                    return path;
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} (+{1} -{2})",
                    path,
                    stats.LinesAdded,
                    stats.LinesDeleted);
            });

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} file(s) changed: {1}",
            filesChanged.Count,
            string.Join(", ", fileFragments));
    }

    private static string? FormatPorcelainLine(StatusEntry entry)
    {
        var status = entry.State;

        if (status == FileStatus.Ignored || status == FileStatus.Unaltered)
        {
            return null;
        }

        if (status.HasFlag(FileStatus.NewInWorkdir) && !status.HasFlag(FileStatus.NewInIndex))
        {
            return $"?? {entry.FilePath}";
        }

        var indexCode = MapIndexCode(status);
        var workingCode = MapWorkingCode(status);

        if (indexCode == ' ' && workingCode == ' ')
        {
            return null;
        }

        return $"{indexCode}{workingCode} {entry.FilePath}";
    }

    private static char MapIndexCode(FileStatus status)
    {
        if (status.HasFlag(FileStatus.NewInIndex)) return 'A';
        if (status.HasFlag(FileStatus.ModifiedInIndex)) return 'M';
        if (status.HasFlag(FileStatus.DeletedFromIndex)) return 'D';
        if (status.HasFlag(FileStatus.RenamedInIndex)) return 'R';
        if (status.HasFlag(FileStatus.TypeChangeInIndex)) return 'T';
        return ' ';
    }

    private static char MapWorkingCode(FileStatus status)
    {
        if (status.HasFlag(FileStatus.ModifiedInWorkdir)) return 'M';
        if (status.HasFlag(FileStatus.DeletedFromWorkdir)) return 'D';
        if (status.HasFlag(FileStatus.RenamedInWorkdir)) return 'R';
        if (status.HasFlag(FileStatus.TypeChangeInWorkdir)) return 'T';
        return ' ';
    }
}
