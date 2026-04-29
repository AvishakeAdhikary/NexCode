using System.Text.Json;

namespace NexCode.Service.Mcp;

/// <summary>
/// Transport abstraction for the Model Context Protocol JSON-RPC dialect. Implementations
/// hide the wire-format particulars (stdio framing, HTTP+SSE, etc.) so the higher-level
/// <see cref="McpClient"/> can speak JSON-RPC uniformly.
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>Raised when the server pushes an unsolicited JSON-RPC notification.</summary>
    event EventHandler<JsonElement>? NotificationReceived;

    /// <summary>Connect to the configured server. Throws if the connection cannot be opened.</summary>
    Task ConnectAsync(JsonElement config, CancellationToken cancellationToken);

    /// <summary>Send a JSON-RPC request and await the matching response.</summary>
    Task<JsonElement> SendRequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken);

    /// <summary>Send a JSON-RPC notification (fire-and-forget).</summary>
    Task SendNotificationAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken);

    /// <summary>Close the underlying transport gracefully.</summary>
    Task DisconnectAsync();
}
