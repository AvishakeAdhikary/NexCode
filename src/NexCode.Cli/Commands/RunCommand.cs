using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Models;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode run "&lt;prompt&gt;"</c> (spec §5.2) — single-shot non-interactive mode.
/// Creates a session, sends one user message, drains the token stream, then exits.
/// </summary>
internal static class RunCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: nexcode run \"<prompt>\" [--mode <mode>] [--no-confirm] [--project <path>]");
            return 1;
        }

        var prompt = string.Join(' ', args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.Error.WriteLine("A non-empty prompt is required.");
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var projectPath = Path.GetFullPath(IpcClient.GetOption(args, "--project") ?? Environment.CurrentDirectory);
        var noConfirm = IpcClient.HasFlag(args, "--no-confirm");
        var mode = SessionCommand.ParseSessionMode(IpcClient.GetOption(args, "--mode"));

        var permission = noConfirm ? PermissionLevel.Full : PermissionLevel.Default;

        var createResponse = await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.SessionCreate,
                new SessionCreateRequest(projectPath, mode, ExecutionMode.Local, permission, SandboxEnabled: false)),
            pipeName,
            cancellationToken);
        if (createResponse.Error is not null) return IpcClient.PrintError(createResponse.Error);

        var session = createResponse.DeserializeResult<SessionCreateResponse>();
        if (session is null)
        {
            Console.Error.WriteLine("Helper service returned an unexpected create response.");
            return 1;
        }

        var sendResponse = await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.SessionSendMessage,
                new SessionSendMessageRequest(session.SessionId, prompt)),
            pipeName,
            cancellationToken);
        if (sendResponse.Error is not null) return IpcClient.PrintError(sendResponse.Error);

        long? sequence = null;
        var idleStreak = 0;
        while (!cancellationToken.IsCancellationRequested && idleStreak < 50)
        {
            var poll = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.ServicePollEvents, new ServiceEventsPollRequest(sequence)),
                pipeName,
                cancellationToken);
            if (poll.Error is not null) return IpcClient.PrintError(poll.Error);

            var events = poll.DeserializeResult<ServiceEventsPollResponse>();
            if (events is null) break;
            sequence = events.LatestSequence;
            var ended = false;
            var any = false;
            foreach (var envelope in events.Events)
            {
                any = true;
                if (envelope.EventType == ServiceEventTypes.Token)
                {
                    var payload = envelope.Payload.Deserialize<TokenEventPayload>(NexCode.Shared.Json.JsonSerialization.Options);
                    if (payload is not null && payload.SessionId == session.SessionId)
                    {
                        Console.Write(payload.Content);
                    }
                }
                else if (envelope.EventType == ServiceEventTypes.SessionEnd)
                {
                    ended = true;
                }
            }

            if (ended) break;
            if (!any)
            {
                idleStreak++;
                await Task.Delay(100, cancellationToken);
            }
            else
            {
                idleStreak = 0;
            }
        }

        Console.WriteLine();
        return 0;
    }
}
