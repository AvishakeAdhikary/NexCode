using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Service;
using NexCode.Service.Permissions;
using NexCode.Service.Tools;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.Permissions;

public sealed class PermissionGateServiceTests
{
    [Fact]
    public async Task DefaultRequirementTool_AutoAllows_WithoutPublishingEvent()
    {
        var hub = new ServiceEventHub();
        var gate = new PermissionGateService(hub, NullLogger<PermissionGateService>.Instance);
        var tool = new FakeTool("read_file", ToolPermissionRequirement.Default);

        var decision = await gate.RequestAsync(
            sessionId: Guid.NewGuid(),
            tool: tool,
            callId: "call-1",
            argumentsPreview: "{}",
            currentMode: PermissionMode.Default,
            cancellationToken: CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(PermissionResponse.AllowOnce, decision.Origin);
        Assert.Empty(hub.Poll(null).Events);
    }

    [Fact]
    public async Task FullMode_AutoAllows_EvenForFullRequirementTools()
    {
        var hub = new ServiceEventHub();
        var gate = new PermissionGateService(hub, NullLogger<PermissionGateService>.Instance);
        var tool = new FakeTool("git_revert", ToolPermissionRequirement.Full);

        var decision = await gate.RequestAsync(
            sessionId: Guid.NewGuid(),
            tool: tool,
            callId: "call-1",
            argumentsPreview: "{\"commit_hash\":\"abc\"}",
            currentMode: PermissionMode.Full,
            cancellationToken: CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(PermissionResponse.AllowOnce, decision.Origin);
        Assert.Empty(hub.Poll(null).Events);
    }

    [Fact]
    public async Task DefaultMode_WarnedTool_PublishesEvent_AndBlocksUntilResponse()
    {
        var hub = new ServiceEventHub();
        var gate = new PermissionGateService(hub, NullLogger<PermissionGateService>.Instance);
        var tool = new FakeTool("write_file", ToolPermissionRequirement.Warned);
        var sessionId = Guid.NewGuid();

        var pending = gate.RequestAsync(
            sessionId,
            tool,
            "call-1",
            "{\"path\":\"foo.cs\"}",
            PermissionMode.Default,
            CancellationToken.None);

        Assert.False(pending.IsCompleted);

        // The permission_request event should be in the hub now.
        var events = hub.Poll(null).Events;
        var permissionEvent = Assert.Single(events);
        Assert.Equal(ServiceEventTypes.PermissionRequest, permissionEvent.EventType);
        var payload = permissionEvent.Payload.Deserialize<PermissionRequestEventPayload>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.SessionId);
        Assert.Equal("write_file", payload.ToolName);
        Assert.Equal("call-1", payload.CallId);
        Assert.Equal("Warned", payload.LevelRequired);

        gate.Respond(sessionId, "call-1", PermissionResponse.AllowOnce);

        var decision = await pending;
        Assert.True(decision.Allowed);
        Assert.Equal(PermissionResponse.AllowOnce, decision.Origin);
    }

    [Fact]
    public async Task AllowForSession_AutoAllowsSubsequentRequests_WithoutEvent()
    {
        var hub = new ServiceEventHub();
        var gate = new PermissionGateService(hub, NullLogger<PermissionGateService>.Instance);
        var tool = new FakeTool("write_file", ToolPermissionRequirement.Warned);
        var sessionId = Guid.NewGuid();

        // First call requires user interaction.
        var first = gate.RequestAsync(sessionId, tool, "call-1", "{}", PermissionMode.Default, CancellationToken.None);
        gate.Respond(sessionId, "call-1", PermissionResponse.AllowForSession);
        var firstDecision = await first;
        Assert.True(firstDecision.Allowed);
        Assert.Equal(PermissionResponse.AllowForSession, firstDecision.Origin);

        var sequenceAfterFirst = hub.Poll(null).LatestSequence;

        // Second call must auto-allow without publishing another event.
        var secondDecision = await gate.RequestAsync(
            sessionId, tool, "call-2", "{}", PermissionMode.Default, CancellationToken.None);

        Assert.True(secondDecision.Allowed);
        Assert.Equal(PermissionResponse.AllowForSession, secondDecision.Origin);
        var newEvents = hub.Poll(sequenceAfterFirst).Events;
        Assert.Empty(newEvents);
    }

    [Fact]
    public async Task DenyAlways_AutoDeniesSubsequentRequests_WithoutEvent()
    {
        var hub = new ServiceEventHub();
        var gate = new PermissionGateService(hub, NullLogger<PermissionGateService>.Instance);
        var tool = new FakeTool("git_revert", ToolPermissionRequirement.Full);
        var sessionId = Guid.NewGuid();

        var first = gate.RequestAsync(sessionId, tool, "call-1", "{}", PermissionMode.Default, CancellationToken.None);
        gate.Respond(sessionId, "call-1", PermissionResponse.DenyAlways);
        var firstDecision = await first;
        Assert.False(firstDecision.Allowed);
        Assert.Equal(PermissionResponse.DenyAlways, firstDecision.Origin);

        var sequenceAfterFirst = hub.Poll(null).LatestSequence;

        var secondDecision = await gate.RequestAsync(
            sessionId, tool, "call-2", "{}", PermissionMode.Default, CancellationToken.None);

        Assert.False(secondDecision.Allowed);
        Assert.Equal(PermissionResponse.DenyAlways, secondDecision.Origin);
        var newEvents = hub.Poll(sequenceAfterFirst).Events;
        Assert.Empty(newEvents);
    }

    [Fact]
    public async Task CancellationToken_CancelsPendingRequest()
    {
        var hub = new ServiceEventHub();
        var gate = new PermissionGateService(hub, NullLogger<PermissionGateService>.Instance);
        var tool = new FakeTool("write_file", ToolPermissionRequirement.Warned);
        using var cts = new CancellationTokenSource();

        var pending = gate.RequestAsync(
            Guid.NewGuid(),
            tool,
            "call-1",
            "{}",
            PermissionMode.Default,
            cts.Token);

        // Confirm the call is parked waiting for a response.
        Assert.False(pending.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    private sealed class FakeTool(string name, ToolPermissionRequirement requirement) : ITool
    {
        public string Name => name;
        public string Description => $"Fake tool '{name}'.";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new { type = "object" });
        public ToolPermissionRequirement PermissionRequirement => requirement;

        public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException("FakeTool is for permission gate tests only.");
    }
}
