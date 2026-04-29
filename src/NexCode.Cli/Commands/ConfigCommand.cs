using System.Text.Json;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode config providers|modes|personalities</c> (spec §22.3 ConfigurationFile editor).
/// MVP exposes <c>list</c> verbs against the helper. Edit verbs print a "use the GUI" hint
/// — full inline editing would clobber the live JSON contracts and is gated behind a
/// proper TUI we'll build in a later slice.
/// </summary>
internal static class ConfigCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var area = args[0].ToLowerInvariant();

        var listMethod = area switch
        {
            "providers" => IpcMethods.ProviderList,
            "modes" => IpcMethods.ModeList,
            "personalities" => IpcMethods.PersonalityList,
            _ => null
        };

        if (listMethod is null)
        {
            PrintUsage();
            return 1;
        }

        var verb = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
        if (verb == "list")
        {
            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(listMethod, new { }),
                pipeName, cancellationToken);
            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        if (verb is "edit" or "set" or "remove")
        {
            Console.Error.WriteLine(
                $"Inline editor stub: use 'nexcode config {area} list' or the WinUI shell to edit.");
            Console.Error.WriteLine(
                "Slice 0019 ships read-only CLI editor surface; full TUI editing arrives in a later slice.");
            return 2;
        }

        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  nexcode config providers list");
        Console.Error.WriteLine("  nexcode config modes list");
        Console.Error.WriteLine("  nexcode config personalities list");
    }
}
