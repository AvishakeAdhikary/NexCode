using System.IO.Pipes;
using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "--version" => PrintVersion(),
            "service" => await HandleServiceCommandAsync(args[1..]),
            "account" => await HandleAccountCommandAsync(args[1..]),
            "session" => await HandleSessionCommandAsync(args[1..]),
            _ => UnknownCommand(args[0])
        };
    }

    private static int PrintVersion()
    {
        Console.WriteLine("NexCode CLI foundation 0.1.0");
        return 0;
    }

    private static async Task<int> HandleServiceCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var pipeName = GetOption(args, "--pipe") ?? "nexcode-service-dev";
        JsonRpcResponse response;

        if (args[0].Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            response = await SendRequestAsync(
                JsonRpcRequest.Create(IpcMethods.ServiceHealth, new { }),
                pipeName,
                CancellationToken.None);

            if (response.Error is not null)
            {
                Console.Error.WriteLine($"{response.Error.Code}: {response.Error.Message}");
                return 1;
            }

            var healthPayload = response.DeserializeResult<ServiceHealthPayload>();
            Console.WriteLine(JsonSerializer.Serialize(healthPayload, JsonSerialization.Options));
            return 0;
        }

        if (!args[0].Equals("events", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 1;
        }

        var afterSequenceRaw = GetOption(args, "--after");
        long? afterSequence = null;
        if (!string.IsNullOrWhiteSpace(afterSequenceRaw))
        {
            afterSequence = long.Parse(afterSequenceRaw);
        }

        response = await SendRequestAsync(
            JsonRpcRequest.Create(IpcMethods.ServicePollEvents, new ServiceEventsPollRequest(afterSequence)),
            pipeName,
            CancellationToken.None);

        if (response.Error is not null)
        {
            Console.Error.WriteLine($"{response.Error.Code}: {response.Error.Message}");
            return 1;
        }

        var eventsPayload = response.DeserializeResult<ServiceEventsPollResponse>();
        Console.WriteLine(JsonSerializer.Serialize(eventsPayload, JsonSerialization.Options));
        return 0;
    }

    private static async Task<int> HandleSessionCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var pipeName = GetOption(args, "--pipe") ?? "nexcode-service-dev";
        if (args[0].Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var projectPath = GetOption(args, "--project") ?? Environment.CurrentDirectory;
            var executionMode = ParseExecutionMode(GetOption(args, "--execution"));
            var sandboxEnabled = HasFlag(args, "--sandbox");

            var request = new SessionCreateRequest(
                ProjectPath: Path.GetFullPath(projectPath),
                Mode: SessionMode.Code,
                ExecutionMode: executionMode,
                PermissionLevel: PermissionLevel.Default,
                SandboxEnabled: sandboxEnabled);

            var response = await SendRequestAsync(
                JsonRpcRequest.Create(IpcMethods.SessionCreate, request),
                pipeName,
                CancellationToken.None);

            if (response.Error is not null)
            {
                Console.Error.WriteLine($"{response.Error.Code}: {response.Error.Message}");
                return 1;
            }

            var payload = response.DeserializeResult<SessionCreateResponse>();
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonSerialization.Options));
            return 0;
        }

        if (args[0].Equals("send-message", StringComparison.OrdinalIgnoreCase))
        {
            var sessionIdRaw = GetOption(args, "--session");
            if (!Guid.TryParse(sessionIdRaw, out var sessionId))
            {
                Console.Error.WriteLine("A valid --session <guid> is required.");
                return 1;
            }

            var content = GetOption(args, "--content");
            if (string.IsNullOrWhiteSpace(content))
            {
                Console.Error.WriteLine("A non-empty --content <text> value is required.");
                return 1;
            }

            var response = await SendRequestAsync(
                JsonRpcRequest.Create(
                    IpcMethods.SessionSendMessage,
                    new SessionSendMessageRequest(sessionId, content)),
                pipeName,
                CancellationToken.None);

            if (response.Error is not null)
            {
                Console.Error.WriteLine($"{response.Error.Code}: {response.Error.Message}");
                return 1;
            }

            var payload = response.DeserializeResult<SessionSendMessageResponse>();
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonSerialization.Options));
            return 0;
        }

        if (args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            var sessionIdRaw = GetOption(args, "--session");
            if (!Guid.TryParse(sessionIdRaw, out var sessionId))
            {
                Console.Error.WriteLine("A valid --session <guid> is required.");
                return 1;
            }

            var reason = GetOption(args, "--reason");
            var response = await SendRequestAsync(
                JsonRpcRequest.Create(
                    IpcMethods.SessionCancel,
                    new SessionCancelRequest(sessionId, reason)),
                pipeName,
                CancellationToken.None);

            if (response.Error is not null)
            {
                Console.Error.WriteLine($"{response.Error.Code}: {response.Error.Message}");
                return 1;
            }

            Console.WriteLine(JsonSerializer.Serialize(response.Result, JsonSerialization.Options));
            return 0;
        }

        PrintUsage();
        return 1;
    }

    private static async Task<int> HandleAccountCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var pipeName = GetOption(args, "--pipe") ?? "nexcode-service-dev";
        var response = args[0].ToLowerInvariant() switch
        {
            "status" => await SendRequestAsync(
                JsonRpcRequest.Create(IpcMethods.AccountGetSnapshot, new { }),
                pipeName,
                CancellationToken.None),
            "refresh-subscription" => await SendRequestAsync(
                JsonRpcRequest.Create(IpcMethods.AccountRefreshSubscription, new AccountRefreshSubscriptionRequest()),
                pipeName,
                CancellationToken.None),
            "sign-in" => await SendRequestAsync(
                JsonRpcRequest.Create(IpcMethods.AccountSignIn, new AccountSignInRequest()),
                pipeName,
                CancellationToken.None),
            _ => null
        };

        if (response is null)
        {
            PrintUsage();
            return 1;
        }

        if (response.Error is not null)
        {
            Console.Error.WriteLine($"{response.Error.Code}: {response.Error.Message}");
            return 1;
        }

        if (args[0].Equals("refresh-subscription", StringComparison.OrdinalIgnoreCase))
        {
            var refreshPayload = response.DeserializeResult<AccountRefreshSubscriptionResponse>();
            Console.WriteLine(JsonSerializer.Serialize(refreshPayload, JsonSerialization.Options));
            return 0;
        }

        var payload = response.DeserializeResult<AccountSnapshotPayload>();
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonSerialization.Options));
        return 0;
    }

    private static async Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        string pipeName,
        CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000, cancellationToken);

        using var reader = new StreamReader(client);
        await using var writer = new StreamWriter(client) { AutoFlush = true };

        var payload = JsonSerializer.Serialize(request, JsonSerialization.Options);
        await writer.WriteLineAsync(payload);

        var responseJson = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("The helper service returned an empty response.");

        return JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("The helper service response could not be deserialized.");
    }

    private static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flagName)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].Equals(flagName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ExecutionMode ParseExecutionMode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return ExecutionMode.Local;
        }

        return rawValue.ToLowerInvariant() switch
        {
            "local" => ExecutionMode.Local,
            "remote" => ExecutionMode.Remote,
            "cloud" => ExecutionMode.Cloud,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rawValue),
                rawValue,
                "Execution mode must be one of: local, remote, cloud.")
        };
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("NexCode CLI foundation commands:");
        Console.WriteLine("  nexcode --version");
        Console.WriteLine("  nexcode service ping [--pipe <name>]");
        Console.WriteLine("  nexcode service events [--after <sequence>] [--pipe <name>]");
        Console.WriteLine("  nexcode account status [--pipe <name>]");
        Console.WriteLine("  nexcode account refresh-subscription [--pipe <name>]");
        Console.WriteLine("  nexcode account sign-in [--pipe <name>]");
        Console.WriteLine("  nexcode session create [--project <path>] [--execution <local|remote|cloud>] [--sandbox] [--pipe <name>]");
        Console.WriteLine("  nexcode session send-message --session <guid> --content <text> [--pipe <name>]");
        Console.WriteLine("  nexcode session cancel --session <guid> [--reason <text>] [--pipe <name>]");
    }
}
