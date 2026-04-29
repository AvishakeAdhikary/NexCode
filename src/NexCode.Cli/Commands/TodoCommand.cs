using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode todo get|check|uncheck &lt;item-id&gt;</c> (spec §5.2 / §11.3).
/// <c>get</c> requires <c>--session &lt;id&gt;</c> and forwards to <see cref="IpcMethods.TodoList"/>.
/// </summary>
internal static class TodoCommand
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

        if (verb == "get")
        {
            if (!Guid.TryParse(IpcClient.GetOption(args, "--session"), out var sessionId))
            {
                Console.Error.WriteLine("nexcode todo get --session <session-id>");
                return 1;
            }
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.TodoList, new TodoListRequest(sessionId)),
                pipeName, cancellationToken);
            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        if (args.Length < 2 || !Guid.TryParse(args[1], out var itemId))
        {
            PrintUsage();
            return 1;
        }

        var method = verb switch
        {
            "check" => IpcMethods.TodoCheckItem,
            "uncheck" => IpcMethods.TodoUncheckItem,
            _ => null
        };

        if (method is null)
        {
            PrintUsage();
            return 1;
        }

        var responseMutation = await IpcClient.SendAsync(
            JsonRpcRequest.Create(method, new TodoItemMutationRequest(itemId)),
            pipeName, cancellationToken);
        if (responseMutation.Error is not null) return IpcClient.PrintError(responseMutation.Error);
        Console.WriteLine(JsonSerializer.Serialize(responseMutation.Result, JsonSerialization.Options));
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  nexcode todo get --session <session-id>");
        Console.Error.WriteLine("  nexcode todo check <item-id>");
        Console.Error.WriteLine("  nexcode todo uncheck <item-id>");
    }
}
