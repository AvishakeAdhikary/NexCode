using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Models;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode chat</c> (spec §5.2) — interactive REPL. Creates a session, then loops
/// reading stdin → <see cref="IpcMethods.SessionSendMessage"/>, polling
/// <see cref="IpcMethods.ServicePollEvents"/> for token chunks and printing them to stdout.
/// </summary>
internal static class ChatCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var pipeName = IpcClient.GetPipeName(args);
        var projectPath = Path.GetFullPath(IpcClient.GetOption(args, "--project") ?? Environment.CurrentDirectory);
        var sandbox = IpcClient.HasFlag(args, "--sandbox");
        var mode = SessionCommand.ParseSessionMode(IpcClient.GetOption(args, "--mode"));

        var createResponse = await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.SessionCreate,
                new SessionCreateRequest(
                    projectPath,
                    mode,
                    ExecutionMode.Local,
                    PermissionLevel.Default,
                    sandbox)),
            pipeName,
            cancellationToken);

        if (createResponse.Error is not null) return IpcClient.PrintError(createResponse.Error);

        var session = createResponse.DeserializeResult<SessionCreateResponse>();
        if (session is null)
        {
            Console.Error.WriteLine("Helper service returned an unexpected response.");
            return 1;
        }

        Console.WriteLine($"NexCode chat — session {session.SessionId:N}. Type /quit to exit, /cancel to abort the current turn.");

        long? lastSequence = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
            if (line.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                await IpcClient.SendAsync(
                    JsonRpcRequest.Create(
                        IpcMethods.SessionCancel,
                        new SessionCancelRequest(session.SessionId, "user_cancel")),
                    pipeName, cancellationToken);
                continue;
            }

            var sendResponse = await IpcClient.SendAsync(
                JsonRpcRequest.Create(
                    IpcMethods.SessionSendMessage,
                    new SessionSendMessageRequest(session.SessionId, line)),
                pipeName,
                cancellationToken);

            if (sendResponse.Error is not null)
            {
                IpcClient.PrintError(sendResponse.Error);
                continue;
            }

            lastSequence = await DrainTokensAsync(pipeName, session.SessionId, lastSequence, cancellationToken);
        }

        return 0;
    }

    private static async Task<long?> DrainTokensAsync(
        string pipeName,
        Guid sessionId,
        long? afterSequence,
        CancellationToken cancellationToken)
    {
        // Spec §5 / §11.2 chat polling — coarse grained, sufficient for CLI.
        var idleStreak = 0;
        while (!cancellationToken.IsCancellationRequested && idleStreak < 30)
        {
            var poll = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.ServicePollEvents, new ServiceEventsPollRequest(afterSequence)),
                pipeName,
                cancellationToken);

            if (poll.Error is not null)
            {
                IpcClient.PrintError(poll.Error);
                break;
            }

            var events = poll.DeserializeResult<ServiceEventsPollResponse>();
            if (events is null) break;

            afterSequence = events.LatestSequence;
            var sawTurnEnd = false;
            var sawAny = false;

            foreach (var envelope in events.Events)
            {
                sawAny = true;
                if (envelope.EventType == ServiceEventTypes.Token)
                {
                    var token = envelope.Payload.Deserialize<TokenEventPayload>(NexCode.Shared.Json.JsonSerialization.Options);
                    if (token is not null && token.SessionId == sessionId)
                    {
                        Console.Write(token.Content);
                    }
                }
                else if (envelope.EventType == ServiceEventTypes.SessionEnd)
                {
                    sawTurnEnd = true;
                }
            }

            if (sawTurnEnd)
            {
                break;
            }
            if (!sawAny)
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
        return afterSequence;
    }
}
