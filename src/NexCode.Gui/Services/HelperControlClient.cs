using System.IO.Pipes;
using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Gui.Services;

public sealed class HelperControlClient
{
    private const string PipeName = "nexcode-service-dev";

    public Task<ServiceHealthPayload> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ServiceHealthPayload>(IpcMethods.ServiceHealth, cancellationToken);
    }

    public Task<ServiceEventsPollResponse> PollEventsAsync(
        long? afterSequence,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ServiceEventsPollResponse>(
            IpcMethods.ServicePollEvents,
            new ServiceEventsPollRequest(afterSequence),
            cancellationToken);
    }

    public Task<AccountSnapshotPayload> GetAccountSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<AccountSnapshotPayload>(IpcMethods.AccountGetSnapshot, cancellationToken);
    }

    public Task<AccountSnapshotPayload> SignInAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<AccountSnapshotPayload>(
            IpcMethods.AccountSignIn,
            new AccountSignInRequest(),
            cancellationToken);
    }

    public Task<AccountRefreshSubscriptionResponse> RefreshSubscriptionAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<AccountRefreshSubscriptionResponse>(
            IpcMethods.AccountRefreshSubscription,
            new AccountRefreshSubscriptionRequest(),
            cancellationToken);
    }

    public Task<SessionCreateResponse> CreateSessionAsync(
        SessionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<SessionCreateResponse>(
            IpcMethods.SessionCreate,
            request,
            cancellationToken);
    }

    public Task<SessionSendMessageResponse> SendMessageAsync(
        SessionSendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<SessionSendMessageResponse>(
            IpcMethods.SessionSendMessage,
            request,
            cancellationToken);
    }

    private static async Task<TPayload> SendRequestAsync<TPayload>(string method, CancellationToken cancellationToken)
    {
        return await SendRequestAsync<TPayload>(method, new { }, cancellationToken);
    }

    private static async Task<TPayload> SendRequestAsync<TPayload>(
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(750, cancellationToken);

        using var reader = new StreamReader(client);
        await using var writer = new StreamWriter(client) { AutoFlush = true };

        var request = JsonRpcRequest.Create(method, payload);
        var requestJson = JsonSerializer.Serialize(request, JsonSerialization.Options);
        await writer.WriteLineAsync(requestJson);

        var responseJson = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("The helper service returned an empty payload.");

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("The helper service response was invalid.");

        if (response.Error is not null)
        {
            throw new HelperRequestException(response.Error.Code, response.Error.Message);
        }

        return response.DeserializeResult<TPayload>()
            ?? throw new InvalidOperationException("The helper service payload was empty.");
    }
}
