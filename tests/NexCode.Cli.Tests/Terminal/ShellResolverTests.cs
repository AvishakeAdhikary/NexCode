using System.IO;
using System.Runtime.Versioning;
using NexCode.Service.Terminal;

namespace NexCode.Cli.Tests.Terminal;

[SupportedOSPlatform("windows")]
public sealed class ShellResolverTests
{
    [Fact]
    public void Resolve_PowerShell_FindsSystem32()
    {
        var resolved = ShellResolver.Resolve("powershell");
        Assert.Equal("powershell", resolved.Logical);
        // On Windows hosts the System32 path always exists.
        Assert.True(
            resolved.Available || string.Equals(Path.GetFileName(resolved.Executable), "powershell.exe", System.StringComparison.OrdinalIgnoreCase),
            $"Expected powershell to be available or named powershell.exe; got '{resolved.Executable}'");
    }

    [Fact]
    public void Resolve_Cmd_FindsExecutable()
    {
        var resolved = ShellResolver.Resolve("cmd");
        Assert.Equal("cmd", resolved.Logical);
        Assert.True(resolved.Executable.EndsWith("cmd.exe", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_CustomPath_OverridesShell()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"nexcode-shell-test-{System.Guid.NewGuid():N}.exe");
        File.WriteAllText(tempPath, "stub");
        try
        {
            var resolved = ShellResolver.Resolve("pwsh", customExecutablePath: tempPath);
            Assert.Equal("custom", resolved.Logical);
            Assert.True(resolved.Available);
            Assert.Equal(tempPath, resolved.Executable);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Resolve_UnknownShell_ReportsUnavailable()
    {
        var resolved = ShellResolver.Resolve("does-not-exist");
        Assert.False(resolved.Available);
    }

    [Fact]
    public void Resolve_Wsl_AppendsDistribution()
    {
        var resolved = ShellResolver.Resolve("wsl", wslDistribution: "Ubuntu");
        Assert.Equal("wsl", resolved.Logical);
        Assert.Contains("--distribution", resolved.Args);
        Assert.Contains("Ubuntu", resolved.Args);
    }
}
