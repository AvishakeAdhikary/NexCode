using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Mcp;

/// <summary>
/// Hosted service that connects all <c>auto_connect=true</c> MCP servers when the helper
/// process boots, and gracefully disconnects them on shutdown. Errors during startup are
/// logged but never crash the host — servers fall back to <c>error</c> status until the
/// user retries via <c>mcp.connect</c>.
/// </summary>
public sealed class McpAutoConnectBootstrap : BackgroundService
{
    private readonly McpManager _manager;
    private readonly ILogger<McpAutoConnectBootstrap> _logger;

    public McpAutoConnectBootstrap(McpManager manager, ILogger<McpAutoConnectBootstrap> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _manager.ConnectAutoAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP auto-connect bootstrap failed.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _manager.ShutdownAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP shutdown encountered errors.");
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
