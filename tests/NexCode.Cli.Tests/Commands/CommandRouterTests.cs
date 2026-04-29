using NexCode.Cli;

namespace NexCode.Cli.Tests.Commands;

/// <summary>
/// Argv parsing for the top-level <see cref="CommandRouter"/>. Each test exercises a
/// surface command that can complete locally without contacting the helper service —
/// commands that require IPC fall through to a connection error which we still
/// classify as a routed result (i.e. exit code is non-zero but the dispatcher did its
/// job). For pure routing assertions we focus on the no-IPC branches:
/// <c>--version</c>, <c>--help</c>, and unknown verbs.
/// </summary>
public sealed class CommandRouterTests
{
    [Fact]
    public async Task ExecuteAsync_NoArgs_PrintsUsageAndReturnsOne()
    {
        var (exit, stdout, _) = await CaptureAsync([]);
        Assert.Equal(1, exit);
        Assert.Contains("NexCode CLI", stdout);
    }

    [Fact]
    public async Task ExecuteAsync_VersionFlag_PrintsVersion()
    {
        var (exit, stdout, _) = await CaptureAsync(["--version"]);
        Assert.Equal(0, exit);
        Assert.Contains("NexCode CLI", stdout);
    }

    [Fact]
    public async Task ExecuteAsync_HelpFlag_PrintsUsageAndReturnsZero()
    {
        var (exit, stdout, _) = await CaptureAsync(["--help"]);
        Assert.Equal(0, exit);
        Assert.Contains("nexcode chat", stdout);
        Assert.Contains("nexcode plan", stdout);
        Assert.Contains("nexcode todo", stdout);
        Assert.Contains("nexcode mcp", stdout);
        Assert.Contains("nexcode git", stdout);
        Assert.Contains("nexcode agent", stdout);
        Assert.Contains("nexcode memory", stdout);
        Assert.Contains("nexcode remote", stdout);
        Assert.Contains("nexcode config", stdout);
        Assert.Contains("nexcode shell", stdout);
        Assert.Contains("/init", stdout);
        Assert.Contains("/model", stdout);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTopLevelVerb_ReturnsOne()
    {
        var (exit, _, stderr) = await CaptureAsync(["nope"]);
        Assert.Equal(1, exit);
        Assert.Contains("Unknown command 'nope'", stderr);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSlashVerb_ReturnsOne()
    {
        var (exit, _, stderr) = await CaptureAsync(["/wat"]);
        Assert.Equal(1, exit);
        Assert.Contains("Unknown command '/wat'", stderr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("todo")]
    [InlineData("memory")]
    [InlineData("agent")]
    [InlineData("mcp")]
    [InlineData("config")]
    public async Task ExecuteAsync_KnownVerbWithoutSubcommand_ReturnsOne(string verb)
    {
        var (exit, _, _) = await CaptureAsync([verb]);
        // Each command emits its own usage and returns 1 when no sub-verb is supplied.
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task ExecuteAsync_RunWithoutPrompt_ReturnsOne()
    {
        var (exit, _, stderr) = await CaptureAsync(["run"]);
        Assert.Equal(1, exit);
        Assert.Contains("Usage: nexcode run", stderr);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> CaptureAsync(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var exit = await CommandRouter.ExecuteAsync(args, cts.Token);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }
}
