using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Service;
using NexCode.Service.Permissions;
using NexCode.Service.Tools;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests.Tools;

public sealed class ToolRegistryTests
{
    [Fact]
    public void DescribeForModel_ReturnsOneDescriptorPerTool()
    {
        var tools = new ITool[] { new StubTool("alpha"), new StubTool("beta", ToolPermissionRequirement.Warned) };
        var registry = new ToolRegistry(tools, new AlwaysAllowGate(), NullLogger<ToolRegistry>.Instance);

        var descriptors = registry.DescribeForModel().ToArray();

        Assert.Equal(2, descriptors.Length);
        Assert.Contains(descriptors, item => item.Name == "alpha");
        Assert.Contains(descriptors, item => item.Name == "beta");
    }

    [Fact]
    public async Task InvokeAsync_RoutesToMatchingTool_AndReturnsItsOutcome()
    {
        var alpha = new StubTool("alpha");
        var beta = new StubTool("beta");
        var registry = new ToolRegistry(new ITool[] { alpha, beta }, new AlwaysAllowGate(), NullLogger<ToolRegistry>.Instance);

        var outcome = await registry.InvokeAsync(BuildContext("call-1"), "beta", "{\"value\":1}", CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Equal("beta", outcome.ToolName);
        Assert.Contains("\"executed\":\"beta\"", outcome.ResultJson);
    }

    [Fact]
    public async Task InvokeAsync_UnknownTool_ReturnsStructuredError()
    {
        var registry = new ToolRegistry(new ITool[] { new StubTool("alpha") }, new AlwaysAllowGate(), NullLogger<ToolRegistry>.Instance);

        var outcome = await registry.InvokeAsync(BuildContext("call-9"), "missing", "{}", CancellationToken.None);

        Assert.True(outcome.IsError);
        using var document = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("unknown_tool", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("missing", document.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task InvokeAsync_PermissionDenied_ReturnsPermissionDeniedPayload()
    {
        var warnedTool = new StubTool("dangerous", ToolPermissionRequirement.Warned);
        var registry = new ToolRegistry(
            new ITool[] { warnedTool },
            new DenyingGate("user_denied"),
            NullLogger<ToolRegistry>.Instance);

        var outcome = await registry.InvokeAsync(BuildContext("call-2"), "dangerous", "{}", CancellationToken.None);

        Assert.True(outcome.IsError);
        using var document = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("permission_denied", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("user_denied", document.RootElement.GetProperty("reason").GetString());
        Assert.False(warnedTool.WasInvoked);
    }

    [Fact]
    public async Task InvokeAsync_ToolThrows_WrapsExceptionAsToolError()
    {
        var throwingTool = new ThrowingTool();
        var registry = new ToolRegistry(new ITool[] { throwingTool }, new AlwaysAllowGate(), NullLogger<ToolRegistry>.Instance);

        var outcome = await registry.InvokeAsync(BuildContext("call-x"), "explode", "{}", CancellationToken.None);

        Assert.True(outcome.IsError);
        using var document = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("tool_exception", document.RootElement.GetProperty("error").GetString());
    }

    private static ToolInvocationContext BuildContext(string callId)
    {
        var session = new SessionRuntimeState(
            SessionId: Guid.NewGuid(),
            Request: new SessionCreateRequest(
                ProjectPath: Path.GetTempPath(),
                Mode: SessionMode.Code,
                ExecutionMode: ExecutionMode.Local,
                PermissionLevel: PermissionLevel.Default,
                SandboxEnabled: false),
            CreatedAt: DateTimeOffset.UtcNow);

        return new ToolInvocationContext(
            Session: session,
            ProjectRoot: session.Request.ProjectPath,
            SandboxEnabled: false,
            PermissionMode: PermissionMode.Default,
            CallId: callId);
    }

    private sealed class StubTool(string name, ToolPermissionRequirement requirement = ToolPermissionRequirement.Default) : ITool
    {
        public string Name { get; } = name;
        public string Description => $"Stub tool '{Name}'.";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new { type = "object" });
        public ToolPermissionRequirement PermissionRequirement { get; } = requirement;
        public bool WasInvoked { get; private set; }

        public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            var json = $"{{\"executed\":\"{Name}\"}}";
            return Task.FromResult(new ToolOutcome(Name, context.CallId, json, IsError: false));
        }
    }

    private sealed class ThrowingTool : ITool
    {
        public string Name => "explode";
        public string Description => "Throws to verify error wrapping.";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new { type = "object" });
        public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

        public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private sealed class AlwaysAllowGate : IPermissionGate
    {
        public Task<PermissionDecision> RequestAsync(Guid sessionId, ITool tool, string callId, string argumentsPreview, PermissionMode currentMode, CancellationToken cancellationToken)
            => Task.FromResult(new PermissionDecision(true, PermissionResponse.AllowOnce, null));

        public void Respond(Guid sessionId, string callId, PermissionResponse response) { }
    }

    private sealed class DenyingGate(string reason) : IPermissionGate
    {
        public Task<PermissionDecision> RequestAsync(Guid sessionId, ITool tool, string callId, string argumentsPreview, PermissionMode currentMode, CancellationToken cancellationToken)
            => Task.FromResult(new PermissionDecision(false, PermissionResponse.DenyOnce, reason));

        public void Respond(Guid sessionId, string callId, PermissionResponse response) { }
    }
}
