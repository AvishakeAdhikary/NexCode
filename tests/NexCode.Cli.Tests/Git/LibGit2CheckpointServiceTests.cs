using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Service.Git;

namespace NexCode.Cli.Tests.Git;

public sealed class LibGit2CheckpointServiceTests
{
    [Fact]
    public async Task CreateAsync_ReturnsNull_ForNonGitDirectory()
    {
        using var temp = new TempDirectory();
        var service = new LibGit2CheckpointService(NullLogger<LibGit2CheckpointService>.Instance);

        var result = await service.CreateAsync(
            projectRoot: temp.Path,
            sessionId: Guid.NewGuid(),
            turnNumber: 1,
            cancellationToken: CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_CommitsCheckpointOnSideBranch_AndReturnsDiff()
    {
        using var temp = new TempDirectory();
        InitRepoWithSeedCommit(temp.Path, out _);

        // Modify the seeded file so the working tree is dirty.
        var trackedPath = Path.Combine(temp.Path, "hello.txt");
        File.WriteAllText(trackedPath, "hello world\nsecond line\n");

        var sessionId = Guid.NewGuid();
        var service = new LibGit2CheckpointService(NullLogger<LibGit2CheckpointService>.Instance);

        var result = await service.CreateAsync(temp.Path, sessionId, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.CommitHash));
        Assert.Contains("hello.txt", result.FilesChanged);
        Assert.False(string.IsNullOrEmpty(result.UnifiedDiff));
        Assert.Contains("hello.txt", result.UnifiedDiff);

        // Verify the checkpoint commit lives on the dedicated side branch and HEAD is unmoved.
        using var repo = new Repository(temp.Path);
        var expectedBranch = $"nexcode/checkpoint/{sessionId}/1";
        var checkpointBranch = repo.Branches[expectedBranch];
        Assert.NotNull(checkpointBranch);
        Assert.Equal(result.CommitHash, checkpointBranch!.Tip.Sha);

        // The user's working branch tip must not be the checkpoint commit.
        Assert.NotEqual(result.CommitHash, repo.Head.Tip.Sha);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsPorcelainShowingModifications()
    {
        using var temp = new TempDirectory();
        InitRepoWithSeedCommit(temp.Path, out _);

        var trackedPath = Path.Combine(temp.Path, "hello.txt");
        File.WriteAllText(trackedPath, "hello world\nupdated content\n");

        var service = new LibGit2CheckpointService(NullLogger<LibGit2CheckpointService>.Instance);
        var porcelain = await service.GetStatusAsync(temp.Path, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(porcelain));
        Assert.Contains("hello.txt", porcelain);
        Assert.Contains("M", porcelain);
    }

    [Fact]
    public async Task RevertAsync_ResetsWorkingTreeToPriorCommit()
    {
        using var temp = new TempDirectory();
        InitRepoWithSeedCommit(temp.Path, out var seedCommitSha);

        var trackedPath = Path.Combine(temp.Path, "hello.txt");
        const string originalContent = "hello world\n";
        File.WriteAllText(trackedPath, "hello world\nadded line\n");

        var service = new LibGit2CheckpointService(NullLogger<LibGit2CheckpointService>.Instance);
        var ok = await service.RevertAsync(temp.Path, seedCommitSha, CancellationToken.None);

        Assert.True(ok);
        // Normalize line endings — Git on Windows applies core.autocrlf when restoring files.
        Assert.Equal(
            originalContent.Replace("\r\n", "\n"),
            File.ReadAllText(trackedPath).Replace("\r\n", "\n"));
    }

    private static void InitRepoWithSeedCommit(string path, out string seedCommitSha)
    {
        Repository.Init(path);

        var trackedPath = Path.Combine(path, "hello.txt");
        File.WriteAllText(trackedPath, "hello world\n");

        using var repo = new Repository(path);
        Commands.Stage(repo, "hello.txt");

        var signature = new Signature("test", "test@example.com", DateTimeOffset.UtcNow);
        var seed = repo.Commit("seed", signature, signature, new CommitOptions { AllowEmptyCommit = false });
        seedCommitSha = seed.Sha;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nexcode-checkpoint-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(Path))
                    {
                        // Clear the read-only attribute that LibGit2 sometimes leaves behind on
                        // pack files so Directory.Delete can complete on Windows.
                        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                File.SetAttributes(file, FileAttributes.Normal);
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }

                        Directory.Delete(Path, recursive: true);
                    }

                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }
            // Best-effort cleanup; tests should still pass even if cleanup is delayed.
        }
    }
}
