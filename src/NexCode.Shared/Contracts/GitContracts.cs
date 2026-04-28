namespace NexCode.Shared.Contracts;

public sealed record GitStatusRequest(string ProjectPath);

public sealed record GitStatusResponse(
    string ProjectPath,
    bool IsRepository,
    string? CurrentBranch,
    string? HeadCommit,
    GitFileStatus[] Files);

public sealed record GitFileStatus(
    string Path,
    string IndexState,
    string WorkingTreeState);

public sealed record GitDiffRequest(
    string ProjectPath,
    string? FromRef,
    string? ToRef);

public sealed record GitDiffResponse(
    string ProjectPath,
    string UnifiedDiff,
    bool IsRepository);

public sealed record GitRevertRequest(
    string ProjectPath,
    string CheckpointCommitHash);

public sealed record GitRevertResponse(
    string ProjectPath,
    bool Success,
    string? Message);
