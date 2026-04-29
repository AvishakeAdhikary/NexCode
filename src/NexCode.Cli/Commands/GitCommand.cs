using System.Diagnostics;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode git &lt;subcommand&gt;</c> (spec §5.2). For the three first-class
/// helper-backed verbs (status / diff / revert) we dispatch via IPC. For the rest we
/// shell out to the host <c>git</c> executable so the user gets full coverage without
/// us re-implementing every porcelain command.
/// </summary>
internal static class GitCommand
{
    private static readonly HashSet<string> HelperBackedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "diff",
        "revert"
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: nexcode git <subcommand> [--project <path>] [args...]");
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var projectPath = Path.GetFullPath(IpcClient.GetOption(args, "--project") ?? Environment.CurrentDirectory);
        var verb = args[0].ToLowerInvariant();

        if (HelperBackedVerbs.Contains(verb))
        {
            return await DispatchHelperAsync(verb, args, projectPath, pipeName, cancellationToken);
        }

        return RunHostGit(args);
    }

    private static async Task<int> DispatchHelperAsync(
        string verb,
        string[] args,
        string projectPath,
        string pipeName,
        CancellationToken cancellationToken)
    {
        try
        {
            var (method, payload) = verb switch
            {
                "status" => (IpcMethods.GitStatus, (object)new GitStatusRequest(projectPath)),
                "diff" => (IpcMethods.GitDiff, new GitDiffRequest(
                    projectPath,
                    IpcClient.GetOption(args, "--from"),
                    IpcClient.GetOption(args, "--to"))),
                "revert" => (IpcMethods.GitRevert, new GitRevertRequest(
                    projectPath,
                    args.Length > 1 ? args[1] : string.Empty)),
                _ => throw new InvalidOperationException($"Unhandled helper verb '{verb}'.")
            };

            var response = await IpcClient.SendAsync(
                JsonRpcRequest.Create(method, payload),
                pipeName,
                cancellationToken);

            if (response.Error is not null) return IpcClient.PrintError(response.Error);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                response.Result,
                NexCode.Shared.Json.JsonSerialization.Options));
            return 0;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Helper service is offline; falling back to host 'git'.");
            return RunHostGit(args);
        }
    }

    private static int RunHostGit(string[] args)
    {
        // Strip CLI-private options before forwarding.
        var forwarded = new List<string>(args.Length);
        for (var index = 0; index < args.Length; index++)
        {
            var candidate = args[index];
            if (candidate.Equals("--pipe", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("--project", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }
            forwarded.Add(candidate);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
        };
        foreach (var arg in forwarded)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Console.Error.WriteLine("Failed to start 'git'.");
                return 1;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git invocation failed: {ex.Message}");
            return 1;
        }
    }
}
