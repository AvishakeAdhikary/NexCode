using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode /model</c> (spec §12.3) — mirrors OpenAI Codex's model selector.
/// Without arguments lists the providers' default models. With <c>?</c>: prints the
/// active default. With an explicit <c>&lt;id&gt;</c>: requests provider.set_default
/// against the helper's provider with a matching <c>default_model_id</c>.
/// </summary>
internal static class SlashModelCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var pipeName = IpcClient.GetPipeName(args);

        var listResponse = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.ProviderList, new { }),
            pipeName, cancellationToken);
        if (listResponse.Error is not null) return IpcClient.PrintError(listResponse.Error);

        var listing = listResponse.DeserializeResult<ProviderListResponse>();
        if (listing is null)
        {
            Console.Error.WriteLine("provider.list returned no payload.");
            return 1;
        }

        if (args.Length == 0)
        {
            PrintProviders(listing);
            return 0;
        }

        var argument = args[0];
        if (argument == "?" || argument.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            var current = listing.Providers.FirstOrDefault(p => p.IsDefault);
            if (current is null)
            {
                Console.WriteLine("No default provider configured.");
                return 0;
            }
            Console.WriteLine($"Active model: {current.DefaultModelId} ({current.ProviderKey})");
            return 0;
        }

        var match = listing.Providers.FirstOrDefault(p =>
            string.Equals(p.DefaultModelId, argument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.ProviderKey, argument, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Console.Error.WriteLine($"No provider exposes model id '{argument}'. Use 'nexcode /model' to list.");
            return 1;
        }

        var setResponse = await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.ProviderSetDefault,
                new ProviderSetDefaultRequest(match.ProviderKey)),
            pipeName, cancellationToken);
        if (setResponse.Error is not null) return IpcClient.PrintError(setResponse.Error);
        Console.WriteLine($"Default model set to {match.DefaultModelId} via provider '{match.ProviderKey}'.");
        Console.WriteLine(JsonSerializer.Serialize(setResponse.Result, JsonSerialization.Options));
        return 0;
    }

    private static void PrintProviders(ProviderListResponse listing)
    {
        if (listing.Providers.Length == 0)
        {
            Console.WriteLine("No providers registered. Use the WinUI shell or 'nexcode config providers list'.");
            return;
        }
        Console.WriteLine("Available models:");
        foreach (var provider in listing.Providers)
        {
            var marker = provider.IsDefault ? "*" : " ";
            Console.WriteLine($" {marker} {provider.DefaultModelId,-32} ({provider.ProviderKey} — {provider.DisplayName})");
        }
        Console.WriteLine();
        Console.WriteLine("Pass a model id to switch: nexcode /model <id>");
    }
}
