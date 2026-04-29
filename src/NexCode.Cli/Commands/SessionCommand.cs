using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode session create|send-message|cancel|list|load|export</c> (spec §5.2 +
/// §40.2 history surface). Calls <see cref="IpcMethods.HistoryList"/> for list/load
/// and renders JSON or Markdown depending on <c>--format</c>.
/// </summary>
internal static class SessionCommand
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

        return verb switch
        {
            "create" => await CreateAsync(args, pipeName, cancellationToken),
            "send-message" => await SendMessageAsync(args, pipeName, cancellationToken),
            "cancel" => await CancelAsync(args, pipeName, cancellationToken),
            "list" => await ListAsync(args, pipeName, cancellationToken),
            "load" => await LoadAsync(args, pipeName, cancellationToken),
            "export" => await ExportAsync(args, pipeName, cancellationToken),
            _ => UnknownVerb(args[0])
        };
    }

    private static async Task<int> CreateAsync(string[] args, string pipeName, CancellationToken token)
    {
        var projectPath = IpcClient.GetOption(args, "--project") ?? Environment.CurrentDirectory;
        var executionMode = ParseExecutionMode(IpcClient.GetOption(args, "--execution"));
        var sandbox = IpcClient.HasFlag(args, "--sandbox");
        var mode = ParseSessionMode(IpcClient.GetOption(args, "--mode"));

        var request = new SessionCreateRequest(
            ProjectPath: Path.GetFullPath(projectPath),
            Mode: mode,
            ExecutionMode: executionMode,
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: sandbox);

        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.SessionCreate, request),
            pipeName,
            token);

        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        return IpcClient.PrintJson(response.DeserializeResult<SessionCreateResponse>());
    }

    private static async Task<int> SendMessageAsync(string[] args, string pipeName, CancellationToken token)
    {
        if (!Guid.TryParse(IpcClient.GetOption(args, "--session"), out var sessionId))
        {
            Console.Error.WriteLine("A valid --session <guid> is required.");
            return 1;
        }
        var content = IpcClient.GetOption(args, "--content");
        if (string.IsNullOrWhiteSpace(content))
        {
            Console.Error.WriteLine("A non-empty --content <text> is required.");
            return 1;
        }
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.SessionSendMessage, new SessionSendMessageRequest(sessionId, content)),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        return IpcClient.PrintJson(response.DeserializeResult<SessionSendMessageResponse>());
    }

    private static async Task<int> CancelAsync(string[] args, string pipeName, CancellationToken token)
    {
        if (!Guid.TryParse(IpcClient.GetOption(args, "--session"), out var sessionId))
        {
            Console.Error.WriteLine("A valid --session <guid> is required.");
            return 1;
        }
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.SessionCancel,
                new SessionCancelRequest(sessionId, IpcClient.GetOption(args, "--reason"))),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> ListAsync(string[] args, string pipeName, CancellationToken token)
    {
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.HistoryList, new { }),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> LoadAsync(string[] args, string pipeName, CancellationToken token)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var id))
        {
            Console.Error.WriteLine("Usage: nexcode session load <session-id>");
            return 1;
        }
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.HistoryList, new { session_id = id }),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> ExportAsync(string[] args, string pipeName, CancellationToken token)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var id))
        {
            Console.Error.WriteLine("Usage: nexcode session export <session-id> [--format json|markdown]");
            return 1;
        }
        var format = (IpcClient.GetOption(args, "--format") ?? "markdown").ToLowerInvariant();
        var response = await IpcClient.SendAsync(
            JsonRpcRequest.Create(IpcMethods.HistoryExport, new { session_id = id, format }),
            pipeName, token);
        if (response.Error is not null) return IpcClient.PrintError(response.Error);
        Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
        return 0;
    }

    private static int UnknownVerb(string verb)
    {
        Console.Error.WriteLine($"Unknown session sub-command '{verb}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  nexcode session create [--project <p>] [--execution local|remote|cloud] [--sandbox] [--mode plan|code|debug|ask]");
        Console.Error.WriteLine("  nexcode session send-message --session <id> --content <text>");
        Console.Error.WriteLine("  nexcode session cancel --session <id> [--reason <text>]");
        Console.Error.WriteLine("  nexcode session list");
        Console.Error.WriteLine("  nexcode session load <id>");
        Console.Error.WriteLine("  nexcode session export <id> [--format json|markdown]");
    }

    public static SessionMode ParseSessionMode(string? raw) => (raw ?? string.Empty).ToLowerInvariant() switch
    {
        "plan" => SessionMode.Plan,
        "code" => SessionMode.Code,
        "debug" => SessionMode.Debug,
        "ask" => SessionMode.Ask,
        _ => SessionMode.Code
    };

    public static ExecutionMode ParseExecutionMode(string? raw) => (raw ?? string.Empty).ToLowerInvariant() switch
    {
        "local" => ExecutionMode.Local,
        "remote" => ExecutionMode.Remote,
        "cloud" => ExecutionMode.Cloud,
        _ => ExecutionMode.Local
    };
}
