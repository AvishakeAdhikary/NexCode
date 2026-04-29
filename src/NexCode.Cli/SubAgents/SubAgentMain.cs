using System.Text.Json;
using NexCode.Shared.Json;

namespace NexCode.Cli.SubAgents;

/// <summary>
/// Spec §19 entry point for child sub-agent processes spawned by
/// <c>NexCode.Service.SubAgents.SubAgentManager</c>. For the slice 0016 deliverable this
/// emits one JSON-RPC notification on stdout to confirm wiring and then exits cleanly.
/// </summary>
internal static class SubAgentMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        var subAgentId = GetOption(args, "--id") ?? string.Empty;
        var parentSessionId = GetOption(args, "--parent") ?? string.Empty;
        var configJson = GetOption(args, "--config") ?? "{}";

        var notification = new
        {
            jsonrpc = "2.0",
            method = "subagent.status",
            @params = new
            {
                id = subAgentId,
                parent = parentSessionId,
                state = "running",
                message = "Sub-agent placeholder running",
                config = configJson,
            },
        };

        var line = JsonSerializer.Serialize(notification, JsonSerialization.Options);
        await Console.Out.WriteLineAsync(line);
        await Console.Out.FlushAsync();

        // The placeholder runner exits cleanly; the parent's MonitorChildAsync will see
        // EOF on stdout and emit AgentEnded.
        return 0;
    }

    private static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        return null;
    }
}
