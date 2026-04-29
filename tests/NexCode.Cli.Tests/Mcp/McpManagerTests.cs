using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexCode.Service;
using NexCode.Service.Mcp;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests.Mcp;

/// <summary>
/// Tests the in-memory lifecycle of <see cref="McpClient"/> against a fake transport.
/// The DB-backed <see cref="McpManager"/> wires these clients into stored
/// <c>McpServerEntity</c> records, but the connect/initialize/list/call cycle is
/// transport-only and is verified here without touching SQLite.
/// </summary>
public sealed class McpManagerTests
{
    [Fact]
    public async Task McpClient_Connect_PerformsHandshakeAndListsTools()
    {
        var transport = new FakeTransport();
        var hub = new ServiceEventHub();
        var client = new McpClient(
            Guid.NewGuid(),
            "fake",
            transport,
            hub,
            NullLogger<McpClient>.Instance);

        await client.ConnectAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        Assert.Equal(McpServerStatuses.Connected, client.Status);
        Assert.Contains(transport.Methods, m => m == "initialize");
        Assert.Contains(transport.Methods, m => m == "tools/list");
        Assert.Single(client.Tools);
        Assert.Equal("echo", client.Tools[0].ToolName);
    }

    [Fact]
    public async Task McpClient_CallTool_ReturnsRawResultAndPublishesEvents()
    {
        var transport = new FakeTransport();
        var hub = new ServiceEventHub();
        var client = new McpClient(
            Guid.NewGuid(),
            "fake",
            transport,
            hub,
            NullLogger<McpClient>.Instance);

        await client.ConnectAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None);
        var args = JsonSerializer.SerializeToElement(new { msg = "hi" });

        var result = await client.CallToolAsync("echo", args, CancellationToken.None);

        Assert.Equal("hi", result.GetProperty("content").GetString());
        var events = hub.Poll(0).Events;
        Assert.Contains(events, e => e.EventType == ServiceEventTypes.McpToolEvent);
        Assert.Contains(events, e => e.EventType == ServiceEventTypes.McpStatus);
    }

    [Fact]
    public async Task McpClient_Disconnect_PublishesStatusEvent()
    {
        var transport = new FakeTransport();
        var hub = new ServiceEventHub();
        var client = new McpClient(
            Guid.NewGuid(),
            "fake",
            transport,
            hub,
            NullLogger<McpClient>.Instance);

        await client.ConnectAsync(JsonDocument.Parse("{}").RootElement, CancellationToken.None);
        await client.DisconnectAsync();

        Assert.Equal(McpServerStatuses.Disconnected, client.Status);
        var statusEvents = hub.Poll(0).Events
            .Where(e => e.EventType == ServiceEventTypes.McpStatus)
            .ToArray();
        Assert.Contains(statusEvents, e =>
            e.Payload.TryGetProperty("status", out var s)
            && s.GetString() == McpServerStatuses.Disconnected);
    }

    private sealed class FakeTransport : IMcpTransport
    {
        public List<string> Methods { get; } = new();
#pragma warning disable CS0067
        public event EventHandler<JsonElement>? NotificationReceived;
#pragma warning restore CS0067

        public Task ConnectAsync(JsonElement config, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<JsonElement> SendRequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
        {
            Methods.Add(method);
            JsonElement result = method switch
            {
                "initialize" => JsonSerializer.SerializeToElement(new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "fake", version = "1.0.0" },
                }),
                "tools/list" => JsonSerializer.SerializeToElement(new
                {
                    tools = new[]
                    {
                        new
                        {
                            name = "echo",
                            description = "echoes input",
                            inputSchema = new { type = "object" },
                        },
                    },
                }),
                "tools/call" => JsonSerializer.SerializeToElement(new
                {
                    content = parameters.TryGetProperty("arguments", out var a) && a.TryGetProperty("msg", out var m)
                        ? m.GetString()
                        : null,
                    isError = false,
                }),
                _ => JsonSerializer.SerializeToElement(new { }),
            };
            return Task.FromResult(result);
        }

        public Task SendNotificationAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
        {
            Methods.Add($"notify:{method}");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
