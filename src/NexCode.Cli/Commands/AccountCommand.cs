using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode account</c> sub-tree (status / sign-in / refresh-subscription) preserved
/// from the original Program.cs and re-rooted under <see cref="CommandRouter"/>.
/// </summary>
internal static class AccountCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: nexcode account status|sign-in|refresh-subscription [--pipe <name>]");
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var verb = args[0].ToLowerInvariant();

        var request = verb switch
        {
            "status" => JsonRpcRequest.Create(IpcMethods.AccountGetSnapshot, new { }),
            "refresh-subscription" => JsonRpcRequest.Create(
                IpcMethods.AccountRefreshSubscription,
                new AccountRefreshSubscriptionRequest()),
            "sign-in" => JsonRpcRequest.Create(IpcMethods.AccountSignIn, new AccountSignInRequest()),
            _ => null
        };

        if (request is null)
        {
            Console.Error.WriteLine($"Unknown account sub-command '{args[0]}'.");
            return 1;
        }

        var response = await IpcClient.SendAsync(request, pipeName, cancellationToken);

        if (response.Error is not null)
        {
            return IpcClient.PrintError(response.Error);
        }

        if (verb == "refresh-subscription")
        {
            return IpcClient.PrintJson(response.DeserializeResult<AccountRefreshSubscriptionResponse>());
        }

        return IpcClient.PrintJson(response.DeserializeResult<AccountSnapshotPayload>());
    }
}
