using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode memory get|set|list</c> (spec §5.2 / §9 memory store).
/// </summary>
internal static class MemoryCommand
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
        var scope = ParseScope(IpcClient.GetOption(args, "--scope"));
        Guid? projectId = TryParseGuid(IpcClient.GetOption(args, "--project-id"));
        Guid? sessionId = TryParseGuid(IpcClient.GetOption(args, "--session-id"));

        return verb switch
        {
            "list" => await ListAsync(pipeName, cancellationToken),
            "get" => await GetAsync(args, scope, projectId, sessionId, pipeName, cancellationToken),
            "set" => await SetAsync(args, scope, projectId, sessionId, pipeName, cancellationToken),
            _ => UnknownVerb(verb)
        };
    }

    private static async Task<int> ListAsync(string pipeName, CancellationToken token)
    {
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.MemoryList, new { }),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> GetAsync(
        string[] args, MemoryScope scope, Guid? projectId, Guid? sessionId, string pipeName, CancellationToken token)
    {
        var key = IpcClient.GetOption(args, "--key");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("nexcode memory get --key <key> [--scope global|project|session]");
            return 1;
        }
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.MemoryRead, new MemoryReadRequest(key!, scope, projectId, sessionId)),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> SetAsync(
        string[] args, MemoryScope scope, Guid? projectId, Guid? sessionId, string pipeName, CancellationToken token)
    {
        var key = IpcClient.GetOption(args, "--key");
        var value = IpcClient.GetOption(args, "--value");
        if (string.IsNullOrWhiteSpace(key) || value is null)
        {
            Console.Error.WriteLine("nexcode memory set --key <k> --value <v> [--scope ...] [--tags csv]");
            return 1;
        }
        var tagsRaw = IpcClient.GetOption(args, "--tags");
        var tags = string.IsNullOrWhiteSpace(tagsRaw)
            ? Array.Empty<string>()
            : tagsRaw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.MemoryWrite,
                new MemoryWriteRequest(key!, value, scope, projectId, sessionId, tags)),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static int UnknownVerb(string verb)
    {
        Console.Error.WriteLine($"Unknown memory sub-command '{verb}'. Use list|get|set.");
        return 1;
    }

    private static MemoryScope ParseScope(string? raw) => (raw ?? string.Empty).ToLowerInvariant() switch
    {
        "project" => MemoryScope.Project,
        "session" => MemoryScope.Session,
        _ => MemoryScope.Global
    };

    private static Guid? TryParseGuid(string? raw) =>
        Guid.TryParse(raw, out var value) ? value : null;

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  nexcode memory list");
        Console.Error.WriteLine("  nexcode memory get --key <k> [--scope global|project|session]");
        Console.Error.WriteLine("  nexcode memory set --key <k> --value <v> [--scope ...] [--tags csv]");
    }
}
