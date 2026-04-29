using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Mcp;

/// <summary>
/// High-level MCP client for a single server. Owns the transport, performs the JSON-RPC
/// <c>initialize</c> handshake, caches <c>tools/list</c>, and provides <c>tools/call</c>
/// against the negotiated session. Reconnection uses bounded exponential backoff and
/// surfaces lifecycle changes via <see cref="ServiceEventHub"/>.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    private readonly Guid _serverId;
    private readonly string _serverName;
    private readonly IMcpTransport _transport;
    private readonly ServiceEventHub _eventHub;
    private readonly ILogger<McpClient> _logger;

    private readonly Lock _stateLock = new();
    private string _status = McpServerStatuses.Disconnected;
    private string? _lastError;
    private List<McpToolDescriptor> _tools = new();

    public McpClient(
        Guid serverId,
        string serverName,
        IMcpTransport transport,
        ServiceEventHub eventHub,
        ILogger<McpClient> logger)
    {
        _serverId = serverId;
        _serverName = serverName;
        _transport = transport;
        _eventHub = eventHub;
        _logger = logger;

        _transport.NotificationReceived += OnNotification;
    }

    public Guid ServerId => _serverId;
    public string ServerName => _serverName;
    public string Status
    {
        get { lock (_stateLock) { return _status; } }
    }
    public IReadOnlyList<McpToolDescriptor> Tools
    {
        get { lock (_stateLock) { return _tools.ToArray(); } }
    }

    public async Task ConnectAsync(JsonElement config, CancellationToken cancellationToken)
    {
        SetStatus(McpServerStatuses.Connecting, error: null);

        const int MaxAttempts = 4;
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _transport.ConnectAsync(config, cancellationToken).ConfigureAwait(false);
                await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);
                await RefreshToolsAsync(cancellationToken).ConfigureAwait(false);
                SetStatus(McpServerStatuses.Connected, error: null);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogWarning(ex, "MCP server {Server} connect attempt {Attempt} failed.", _serverName, attempt);
                if (attempt < MaxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));
                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                }
            }
        }

        SetStatus(McpServerStatuses.Error, last?.Message ?? "Failed to connect.");
        throw new InvalidOperationException(
            $"Could not connect to MCP server '{_serverName}': {last?.Message}", last);
    }

    public async Task<JsonElement> CallToolAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var paramsObj = JsonSerializer.SerializeToElement(new
        {
            name = toolName,
            arguments,
        });

        _eventHub.Publish(
            ServiceEventTypes.McpToolEvent,
            new McpToolEventPayload(_serverId, toolName, "started", null));

        try
        {
            var result = await _transport
                .SendRequestAsync("tools/call", paramsObj, cancellationToken)
                .ConfigureAwait(false);

            _eventHub.Publish(
                ServiceEventTypes.McpToolEvent,
                new McpToolEventPayload(_serverId, toolName, "completed", result.GetRawText()));

            return result;
        }
        catch (Exception ex)
        {
            _eventHub.Publish(
                ServiceEventTypes.McpToolEvent,
                new McpToolEventPayload(_serverId, toolName, "failed", ex.Message));
            throw;
        }
    }

    public async Task RefreshToolsAsync(CancellationToken cancellationToken)
    {
        var empty = JsonSerializer.SerializeToElement(new { });
        var result = await _transport
            .SendRequestAsync("tools/list", empty, cancellationToken)
            .ConfigureAwait(false);

        var list = new List<McpToolDescriptor>();
        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("tools", out var toolsEl)
            && toolsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in toolsEl.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var name = tool.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var desc = tool.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                JsonElement schema;
                if (tool.TryGetProperty("inputSchema", out var s) && s.ValueKind != JsonValueKind.Null)
                {
                    schema = s.Clone();
                }
                else if (tool.TryGetProperty("input_schema", out var s2) && s2.ValueKind != JsonValueKind.Null)
                {
                    schema = s2.Clone();
                }
                else
                {
                    schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();
                }
                list.Add(new McpToolDescriptor(_serverId, name, desc, schema));
            }
        }

        lock (_stateLock)
        {
            _tools = list;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _transport.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            SetStatus(McpServerStatuses.Disconnected, error: null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _transport.NotificationReceived -= OnNotification;
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private async Task PerformHandshakeAsync(CancellationToken cancellationToken)
    {
        var initParams = JsonSerializer.SerializeToElement(new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { tools = new { } },
            clientInfo = new { name = "NexCode", version = "0.1.0" },
        });

        await _transport
            .SendRequestAsync("initialize", initParams, cancellationToken)
            .ConfigureAwait(false);

        var notifParams = JsonSerializer.SerializeToElement(new { });
        await _transport
            .SendNotificationAsync("notifications/initialized", notifParams, cancellationToken)
            .ConfigureAwait(false);
    }

    private void SetStatus(string status, string? error)
    {
        lock (_stateLock)
        {
            _status = status;
            _lastError = error;
        }

        _eventHub.Publish(
            ServiceEventTypes.McpStatus,
            new McpStatusEventPayload(_serverId, status, error));
    }

    private void OnNotification(object? sender, JsonElement payload)
    {
        var method = payload.TryGetProperty("method", out var m) ? m.GetString() : null;
        if (string.Equals(method, "notifications/tools/list_changed", StringComparison.Ordinal))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshToolsAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MCP tools/list refresh failed for {Server}.", _serverName);
                }
            });
        }
    }
}
