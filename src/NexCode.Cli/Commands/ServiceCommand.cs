using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode service ping</c> / <c>nexcode service events</c> — preserved from Slice 0017
/// for backwards compatibility.
/// </summary>
internal static class ServiceCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: nexcode service ping|events [--after <seq>] [--pipe <name>]");
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var verb = args[0].ToLowerInvariant();

        if (verb == "ping")
        {
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.ServiceHealth, new { }),
                pipeName,
                cancellationToken);

            if (response.Error is not null)
            {
                return IpcClient.PrintError(response.Error);
            }

            return IpcClient.PrintJson(response.DeserializeResult<ServiceHealthPayload>());
        }

        if (verb == "events")
        {
            long? after = null;
            if (IpcClient.TryGetOption(args, "--after", out var rawAfter)
                && long.TryParse(rawAfter, out var parsed))
            {
                after = parsed;
            }

            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.ServicePollEvents, new ServiceEventsPollRequest(after)),
                pipeName,
                cancellationToken);

            if (response.Error is not null)
            {
                return IpcClient.PrintError(response.Error);
            }

            return IpcClient.PrintJson(response.DeserializeResult<ServiceEventsPollResponse>());
        }

        Console.Error.WriteLine($"Unknown service sub-command '{args[0]}'.");
        return 1;
    }
}
