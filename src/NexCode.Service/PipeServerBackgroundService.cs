using System.IO.Pipes;
using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using NexCode.Data.Repositories;
using NexCode.Service.Git;
using NexCode.Service.Permissions;
using NexCode.Service.Providers;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service;

[SupportedOSPlatform("windows")]
public sealed class PipeServerBackgroundService(
    ILogger<PipeServerBackgroundService> logger,
    IOptions<ServiceHostOptions> options,
    Auth.AccountStateService accountStateService,
    ServiceEventHub serviceEventHub,
    SessionRegistry sessionRegistry,
    ISessionRepository sessionRepository,
    SessionTurnService sessionTurnService,
    ProviderConfigurationService providerConfiguration,
    IPermissionGate permissionGate,
    ICheckpointService checkpointService) : BackgroundService
{
    private const int SubscriptionGateRequiredError = -32021;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NexCode helper service listening on pipe {PipeName}", options.Value.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                options.Value.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(stoppingToken);
            await HandleConnectionAsync(pipe, stoppingToken);
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        JsonRpcResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestLine, JsonSerialization.Options)
                ?? throw new InvalidOperationException("Request payload was empty.");

            response = await HandleRequestAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process named pipe request.");
            response = JsonRpcResponse.Failure(null, -32603, ex.Message);
        }

        var responseJson = JsonSerializer.Serialize(response, JsonSerialization.Options);
        await writer.WriteLineAsync(responseJson);
    }

    private async Task<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            IpcMethods.ServiceHealth => JsonRpcResponse.Success(
                request.Id,
                new ServiceHealthPayload(
                    State: ServiceHealthState.Healthy,
                    Version: options.Value.Version,
                    ActiveSessions: sessionRegistry.Count,
                    Timestamp: DateTimeOffset.UtcNow,
                    PipeName: options.Value.PipeName)),
            IpcMethods.ServicePollEvents => JsonRpcResponse.Success(
                request.Id,
                HandleServicePollEvents(request)),
            IpcMethods.AccountGetSnapshot => JsonRpcResponse.Success(
                request.Id,
                await accountStateService.GetSnapshotAsync(cancellationToken)),
            IpcMethods.AccountSignIn => await HandleAccountSignInAsync(request, cancellationToken),
            IpcMethods.AccountRefreshSubscription => await HandleAccountRefreshSubscriptionAsync(request, cancellationToken),
            IpcMethods.SessionCreate => await HandleSessionCreateAsync(request, cancellationToken),
            IpcMethods.SessionSendMessage => await HandleSessionSendMessageAsync(request, cancellationToken),
            IpcMethods.SessionCancel => HandleSessionCancel(request),
            IpcMethods.ProviderList => JsonRpcResponse.Success(
                request.Id,
                await providerConfiguration.ListAsync(cancellationToken)),
            IpcMethods.ProviderUpsert => await HandleProviderUpsertAsync(request, cancellationToken),
            IpcMethods.ProviderRemove => await HandleProviderRemoveAsync(request, cancellationToken),
            IpcMethods.ProviderSetDefault => await HandleProviderSetDefaultAsync(request, cancellationToken),
            IpcMethods.PermissionRespond => HandlePermissionRespond(request),
            IpcMethods.GitStatus => await HandleGitStatusAsync(request, cancellationToken),
            IpcMethods.GitDiff => await HandleGitDiffAsync(request, cancellationToken),
            IpcMethods.GitRevert => await HandleGitRevertAsync(request, cancellationToken),
            _ => JsonRpcResponse.Failure(request.Id, -32601, $"Unknown method '{request.Method}'.")
        };
    }

    private async Task<JsonRpcResponse> HandleProviderUpsertAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<ProviderUpsertRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProviderKey))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid provider.upsert payload.");
        }

        var response = await providerConfiguration.UpsertAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleProviderRemoveAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<ProviderRemoveRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProviderKey))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid provider.remove payload.");
        }

        var removed = await providerConfiguration.RemoveAsync(payload.ProviderKey, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { providerKey = payload.ProviderKey, removed });
    }

    private async Task<JsonRpcResponse> HandleProviderSetDefaultAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<ProviderSetDefaultRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProviderKey))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid provider.set_default payload.");
        }

        var ok = await providerConfiguration.SetDefaultAsync(payload.ProviderKey, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { providerKey = payload.ProviderKey, isDefault = ok });
    }

    private JsonRpcResponse HandlePermissionRespond(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<PermissionRespondRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CallId) || string.IsNullOrWhiteSpace(payload.Decision))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid permission.respond payload.");
        }

        if (!Enum.TryParse<PermissionResponse>(payload.Decision, ignoreCase: true, out var decision))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, $"Unknown permission decision '{payload.Decision}'.");
        }

        permissionGate.Respond(payload.SessionId, payload.CallId, decision);
        return JsonRpcResponse.Success(
            request.Id,
            new PermissionRespondResponse(payload.SessionId, payload.CallId, true));
    }

    private async Task<JsonRpcResponse> HandleGitStatusAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<GitStatusRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProjectPath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid git.status payload.");
        }

        var porcelain = await checkpointService.GetStatusAsync(payload.ProjectPath, cancellationToken);
        var isRepo = !string.IsNullOrEmpty(porcelain) && !porcelain.StartsWith("not_a_repository", StringComparison.Ordinal);
        var files = ParsePorcelain(porcelain);
        return JsonRpcResponse.Success(
            request.Id,
            new GitStatusResponse(
                ProjectPath: payload.ProjectPath,
                IsRepository: isRepo,
                CurrentBranch: null,
                HeadCommit: null,
                Files: files));
    }

    private async Task<JsonRpcResponse> HandleGitDiffAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<GitDiffRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProjectPath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid git.diff payload.");
        }

        var diff = await checkpointService.GetDiffAsync(
            payload.ProjectPath,
            payload.FromRef ?? "HEAD",
            payload.ToRef ?? string.Empty,
            cancellationToken);
        return JsonRpcResponse.Success(
            request.Id,
            new GitDiffResponse(payload.ProjectPath, diff, IsRepository: !string.IsNullOrEmpty(diff)));
    }

    private async Task<JsonRpcResponse> HandleGitRevertAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<GitRevertRequest>();
        if (payload is null
            || string.IsNullOrWhiteSpace(payload.ProjectPath)
            || string.IsNullOrWhiteSpace(payload.CheckpointCommitHash))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid git.revert payload.");
        }

        var ok = await checkpointService.RevertAsync(
            payload.ProjectPath,
            payload.CheckpointCommitHash,
            cancellationToken);
        return JsonRpcResponse.Success(
            request.Id,
            new GitRevertResponse(payload.ProjectPath, ok, ok ? null : "Checkpoint commit not found or revert failed."));
    }

    private static GitFileStatus[] ParsePorcelain(string porcelain)
    {
        if (string.IsNullOrEmpty(porcelain))
        {
            return Array.Empty<GitFileStatus>();
        }

        var lines = porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = new List<GitFileStatus>(lines.Length);
        foreach (var line in lines)
        {
            if (line.Length < 4)
            {
                continue;
            }

            var indexState = line[0].ToString();
            var workingState = line[1].ToString();
            var path = line[3..].Trim();
            entries.Add(new GitFileStatus(path, indexState, workingState));
        }

        return entries.ToArray();
    }

    private ServiceEventsPollResponse HandleServicePollEvents(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<ServiceEventsPollRequest>() ?? new ServiceEventsPollRequest(null);
        return serviceEventHub.Poll(payload.AfterSequence);
    }

    private async Task<JsonRpcResponse> HandleAccountSignInAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<AccountSignInRequest>() ?? new AccountSignInRequest();
        if (!payload.ForceInteractive)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "account.sign_in currently requires ForceInteractive=true.");
        }

        var snapshot = await accountStateService.SignInAsync(cancellationToken);
        return JsonRpcResponse.Success(request.Id, snapshot);
    }

    private async Task<JsonRpcResponse> HandleAccountRefreshSubscriptionAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<AccountRefreshSubscriptionRequest>()
                      ?? new AccountRefreshSubscriptionRequest();
        if (!payload.ForceRefresh)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "account.refresh_subscription currently requires ForceRefresh=true.");
        }

        var response = await accountStateService.RefreshSubscriptionAsync(cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleSessionCreateAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<SessionCreateRequest>();
        if (payload is null)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid session.create payload.");
        }

        var snapshot = await accountStateService.GetSnapshotAsync(cancellationToken);
        var validation = Auth.SubscriptionCapabilityPolicy.ValidateSessionRequest(
            payload,
            snapshot.Capabilities,
            sessionRegistry.Count);
        if (!validation.Allowed)
        {
            return JsonRpcResponse.Failure(request.Id, SubscriptionGateRequiredError, validation.Message);
        }

        var response = sessionRegistry.Create(payload);
        await sessionRepository.PersistSessionCreatedAsync(
            response.SessionId,
            payload,
            cancellationToken);
        serviceEventHub.Publish(
            ServiceEventTypes.SessionLifecycle,
            new SessionLifecycleEventPayload(
                SessionId: response.SessionId,
                State: "created",
                ProjectPath: payload.ProjectPath,
                Mode: payload.Mode.ToString(),
                ExecutionMode: payload.ExecutionMode.ToString(),
                Reason: null));

        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleSessionSendMessageAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<SessionSendMessageRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Content))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "A valid session.send_message payload with non-empty content is required.");
        }

        if (!sessionRegistry.TryGet(payload.SessionId, out var session) || session is null)
        {
            return JsonRpcResponse.Failure(request.Id, -32004, $"Session '{payload.SessionId}' was not found.");
        }

        var turnShell = await sessionRepository.CreateTurnShellAsync(
            payload.SessionId,
            payload.Content,
            cancellationToken);

        sessionTurnService.StartTurn(session, turnShell.AssistantMessageId, payload.Content);

        return JsonRpcResponse.Success(
            request.Id,
            new SessionSendMessageResponse(
                SessionId: payload.SessionId,
                UserMessageId: turnShell.UserMessageId,
                AssistantMessageId: turnShell.AssistantMessageId,
                AcceptedAt: turnShell.CreatedAt));
    }

    private JsonRpcResponse HandleSessionCancel(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<SessionCancelRequest>();
        if (payload is null)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid session.cancel payload.");
        }

        if (!sessionRegistry.Cancel(payload.SessionId))
        {
            return JsonRpcResponse.Failure(request.Id, -32004, $"Session '{payload.SessionId}' was not found.");
        }

        serviceEventHub.Publish(
            ServiceEventTypes.SessionLifecycle,
            new SessionLifecycleEventPayload(
                SessionId: payload.SessionId,
                State: "cancelled",
                ProjectPath: string.Empty,
                Mode: string.Empty,
                ExecutionMode: string.Empty,
                Reason: payload.Reason));

        return JsonRpcResponse.Success(request.Id, new { cancelled = true, payload.SessionId });
    }
}
