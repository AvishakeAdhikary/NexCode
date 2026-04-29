using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Service;
using NexCode.Service.Terminal;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.Terminal;

[SupportedOSPlatform("windows")]
public sealed class TerminalManagerTests
{
    [Fact]
    [Trait("Category", "RequiresPty")]
    public async Task SpawnAndKill_RoundTripsLifecycle()
    {
        var hub = new ServiceEventHub();
        var manager = new TerminalManager(NullLogger<TerminalManager>.Instance, hub);

        var spawn = manager.Spawn(new TerminalSpawnRequest(
            ProjectPath: System.Environment.CurrentDirectory,
            Shell: "cmd",
            WorkingDirectory: null,
            Cols: 80,
            Rows: 24,
            EnvVars: null));

        if (!spawn.Started)
        {
            // ConPTY may not be available in CI containers.
            Assert.False(string.IsNullOrEmpty(spawn.Error));
            return;
        }

        Assert.False(string.IsNullOrEmpty(spawn.TerminalId));
        Assert.Equal(1, manager.ActiveCount);

        var killResult = await manager.KillAsync(new TerminalKillRequest(spawn.TerminalId));
        Assert.True(killResult.Killed);
    }

    [Fact]
    public async Task Write_InvalidId_ReturnsError()
    {
        var hub = new ServiceEventHub();
        var manager = new TerminalManager(NullLogger<TerminalManager>.Instance, hub);

        var response = await manager.WriteAsync(new TerminalWriteRequest(
            TerminalId: "not-a-guid",
            DataBase64: System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("ls\n")),
            Cols: null,
            Rows: null));

        Assert.False(response.Accepted);
        Assert.Equal("invalid_terminal_id", response.Error);
    }

    [Fact]
    public async Task Kill_UnknownId_ReturnsError()
    {
        var hub = new ServiceEventHub();
        var manager = new TerminalManager(NullLogger<TerminalManager>.Instance, hub);

        var response = await manager.KillAsync(new TerminalKillRequest(System.Guid.NewGuid().ToString("N")));

        Assert.False(response.Killed);
        Assert.Equal("terminal_not_found", response.Error);
    }
}
