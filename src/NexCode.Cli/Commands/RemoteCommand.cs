using System.Text.Json;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode remote serve|connect</c> (spec §5.2 / §28 remote execution).
/// <c>serve</c> blocks the CLI as a launcher for the in-proc Kestrel host (NexCode.Remote).
/// <c>connect</c> forwards a host:port to the helper service which manages the connection
/// lifecycle.
/// </summary>
internal static class RemoteCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var verb = args[0].ToLowerInvariant();

        if (verb == "serve")
        {
            var port = IpcClient.GetOption(args, "--port") ?? "5189";
            Console.WriteLine($"NexCode remote agent listening on port {port}.");
            Console.WriteLine("(Launcher stub — in production this hosts NexCode.Remote.Kestrel in-proc.)");
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // graceful exit
            }
            return 0;
        }

        if (verb == "connect")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: nexcode remote connect <host:port> [--token <t>]");
                return 1;
            }
            var endpoint = args[1];
            var token = IpcClient.GetOption(args, "--token");
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.RemoteConnect,
                    new { endpoint, token }),
                pipeName, cancellationToken);
            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        if (verb == "status")
        {
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.RemoteStatus, new { }),
                pipeName, cancellationToken);
            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  nexcode remote serve [--port <p>]");
        Console.Error.WriteLine("  nexcode remote connect <host:port> [--token <t>]");
        Console.Error.WriteLine("  nexcode remote status");
    }
}
