using System.Text.Json;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode mcp serve</c> / <c>nexcode mcp connect</c> (spec §5.2 + §16).
/// Serve scaffold prints a startup banner and blocks until cancelled — full MCP protocol
/// wiring lives in NexCode.Service.Mcp; this CLI surface is the user-facing entry point.
/// </summary>
internal static class McpCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var verb = args[0].ToLowerInvariant();
        var pipeName = IpcClient.GetPipeName(args);

        if (verb == "serve")
        {
            return await ServeAsync(args, cancellationToken);
        }

        if (verb == "connect")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: nexcode mcp connect <config-json-path>");
                return 1;
            }
            var configPath = args[1];
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"Config file '{configPath}' not found.");
                return 1;
            }
            var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
            using var configDoc = JsonDocument.Parse(configJson);
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.McpConnect, configDoc.RootElement),
                pipeName, cancellationToken);
            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        PrintUsage();
        return 1;
    }

    private static async Task<int> ServeAsync(string[] args, CancellationToken cancellationToken)
    {
        var stdio = IpcClient.HasFlag(args, "--stdio");
        var port = IpcClient.GetOption(args, "--port") ?? "0";

        Console.WriteLine(stdio
            ? "MCP server listening on stdio. Send JSON-RPC frames followed by newlines."
            : $"MCP server listening on port {port} (loopback only).");
        Console.WriteLine("Available tools wrap the helper service tool registry. Press Ctrl+C to stop.");

        if (stdio)
        {
            string? line;
            while (!cancellationToken.IsCancellationRequested
                   && (line = Console.ReadLine()) is not null)
            {
                // Spec §16: a real implementation forwards JSON-RPC to NexCode.Service.Mcp.
                // For Slice 0019 the CLI surface only echoes a stub error so callers can see
                // the wire is alive while the server-side engine is implemented.
                var response = JsonRpcResponse.Failure(
                    id: null,
                    code: -32601,
                    message: "mcp.serve_pending: stdio bridge to helper not yet implemented");
                Console.WriteLine(JsonSerializer.Serialize(response, JsonSerialization.Options));
            }
            return 0;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  nexcode mcp serve [--port <p>] [--stdio]");
        Console.Error.WriteLine("  nexcode mcp connect <config-json-path>");
    }
}
