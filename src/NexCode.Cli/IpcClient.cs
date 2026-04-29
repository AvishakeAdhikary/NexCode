using System.IO.Pipes;
using System.Text.Json;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Cli;

/// <summary>
/// Shared helper that sends a JSON-RPC request to the helper service over a named pipe.
/// Centralizes the connect/serialize/read-line cycle used by every CLI sub-command.
/// </summary>
internal static class IpcClient
{
    public const string DefaultPipeName = "nexcode-service-dev";

    public static async Task<JsonRpcResponse> SendAsync(
        JsonRpcRequest request,
        string pipeName,
        CancellationToken cancellationToken,
        int connectTimeoutMs = 5_000)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(connectTimeoutMs, cancellationToken);

        using var reader = new StreamReader(client);
        await using var writer = new StreamWriter(client) { AutoFlush = true };

        var payload = JsonSerializer.Serialize(request, JsonSerialization.Options);
        await writer.WriteLineAsync(payload);

        var responseJson = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("The helper service returned an empty response.");

        return JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("The helper service response could not be deserialized.");
    }

    public static bool TryGetOption(IReadOnlyList<string> args, string optionName, out string? value)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                value = args[index + 1];
                return true;
            }
        }

        value = null;
        return false;
    }

    public static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        TryGetOption(args, optionName, out var value);
        return value;
    }

    public static bool HasFlag(IReadOnlyList<string> args, string flagName)
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

    public static string GetPipeName(IReadOnlyList<string> args) =>
        GetOption(args, "--pipe") ?? DefaultPipeName;

    public static int PrintError(JsonRpcError error)
    {
        Console.Error.WriteLine($"{error.Code}: {error.Message}");
        return 1;
    }

    public static int PrintJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonSerialization.Options));
        return 0;
    }
}
