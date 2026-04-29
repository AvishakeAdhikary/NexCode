using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode plan get|confirm|reject &lt;id&gt;</c> (spec §5.2 / §10.2 / §11).
/// </summary>
internal static class PlanCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: nexcode plan get|confirm|reject <plan-id> [--reason <text>]");
            return 1;
        }

        var verb = args[0].ToLowerInvariant();
        if (!Guid.TryParse(args[1], out var planId))
        {
            Console.Error.WriteLine("Plan id must be a GUID.");
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);

        return verb switch
        {
            "get" => await GetAsync(planId, pipeName, cancellationToken),
            "confirm" => await ConfirmAsync(planId, pipeName, cancellationToken),
            "reject" => await RejectAsync(planId, IpcClient.GetOption(args, "--reason"), pipeName, cancellationToken),
            _ => UnknownVerb(verb)
        };
    }

    private static async Task<int> GetAsync(Guid id, string pipeName, CancellationToken token)
    {
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.PlanGet, new PlanGetRequest(id)),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> ConfirmAsync(Guid id, string pipeName, CancellationToken token)
    {
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.PlanConfirm, new PlanConfirmRequest(id)),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> RejectAsync(Guid id, string? reason, string pipeName, CancellationToken token)
    {
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.PlanReject, new PlanRejectRequest(id, reason)),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static int UnknownVerb(string verb)
    {
        Console.Error.WriteLine($"Unknown plan sub-command '{verb}'. Use get|confirm|reject.");
        return 1;
    }
}
