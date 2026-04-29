using System.Text.Json;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode agent spawn|kill</c> (spec §5.2 / §17 sub-agents).
/// Spawn forwards a free-form config JSON document to the helper's
/// <see cref="IpcMethods.SubAgentSpawn"/> handler.
/// </summary>
internal static class AgentCommand
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

        if (verb == "spawn")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: nexcode agent spawn <config-json-or-path>");
                return 1;
            }
            var raw = args[1];
            string configJson;
            if (File.Exists(raw))
            {
                configJson = await File.ReadAllTextAsync(raw, cancellationToken);
            }
            else
            {
                configJson = raw;
            }

            JsonDocument configDoc;
            try
            {
                configDoc = JsonDocument.Parse(configJson);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Invalid JSON config: {ex.Message}");
                return 1;
            }

            try
            {
                var response = await IpcClient.SendAsync(
                    JsonRpcRequest.Create(IpcMethods.SubAgentSpawn, configDoc.RootElement),
                    pipeName, cancellationToken);
                if (response.Error is not null) return IpcClient.PrintError(response.Error);
                Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
                return 0;
            }
            finally
            {
                configDoc.Dispose();
            }
        }

        if (verb == "kill")
        {
            if (args.Length < 2 || !Guid.TryParse(args[1], out var agentId))
            {
                Console.Error.WriteLine("Usage: nexcode agent kill <agent-id>");
                return 1;
            }
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.SubAgentKill, new { agent_id = agentId }),
                pipeName, cancellationToken);
            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        if (verb == "list")
        {
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.SubAgentList, new { }),
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
        Console.Error.WriteLine("  nexcode agent spawn <config-json-or-path>");
        Console.Error.WriteLine("  nexcode agent kill <agent-id>");
        Console.Error.WriteLine("  nexcode agent list");
    }
}
