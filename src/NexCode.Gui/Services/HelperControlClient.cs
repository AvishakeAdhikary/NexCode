using System.IO.Pipes;
using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;

namespace NexCode.Gui.Services;

public sealed class HelperControlClient
{
    private const string PipeName = "nexcode-service-dev";

    // The helper serves connections concurrently, so a connect normally succeeds
    // immediately. These values only matter in the rare window where every server
    // instance is momentarily busy: give each attempt enough time and retry a few
    // times with a short backoff rather than failing fast into "Helper unavailable".
    private const int ConnectTimeoutMilliseconds = 1500;
    private const int MaxConnectAttempts = 3;

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

    public Task<ProviderListResponse> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ProviderListResponse>(IpcMethods.ProviderList, cancellationToken);
    }

    public Task<ProviderUpsertResponse> UpsertProviderAsync(
        ProviderUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ProviderUpsertResponse>(IpcMethods.ProviderUpsert, request, cancellationToken);
    }

    public Task<ProviderRemoveResponse> RemoveProviderAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ProviderRemoveResponse>(
            IpcMethods.ProviderRemove,
            new ProviderRemoveRequest(providerKey),
            cancellationToken);
    }

    public Task<ProviderSetDefaultResponse> SetDefaultProviderAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ProviderSetDefaultResponse>(
            IpcMethods.ProviderSetDefault,
            new ProviderSetDefaultRequest(providerKey),
            cancellationToken);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await client.ConnectAsync(ConnectTimeoutMilliseconds, cancellationToken);
                return client;
            }
            catch (Exception ex) when (attempt < MaxConnectAttempts && ex is TimeoutException or IOException)
            {
                // Every server instance was momentarily busy. Back off briefly and retry
                // before surfacing the failure as "Helper unavailable".
                await client.DisposeAsync();
                await Task.Delay(150 * attempt, cancellationToken);
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }
        }
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
        await using var client = await ConnectAsync(cancellationToken);

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
