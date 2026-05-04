using System.IO.Pipes;
using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using NexCode.Data.Repositories;
using NexCode.Service.Automations;
using NexCode.Service.BackgroundServiceMode;
using NexCode.Service.Cloud;
using NexCode.Service.Environments;
using NexCode.Service.Git;
using NexCode.Service.History;
using NexCode.Service.Lsp;
using NexCode.Service.Mcp;
using NexCode.Service.Memory;
using NexCode.Service.Modes;
using NexCode.Service.Permissions;
using NexCode.Service.Personalities;
using NexCode.Service.Plans;
using NexCode.Service.Plugins;
using NexCode.Service.Providers;
using NexCode.Service.Remote;
using NexCode.Service.SubAgents;
using NexCode.Service.Telemetry;
using NexCode.Service.Terminal;
using NexCode.Service.Tools;
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
    ICheckpointService checkpointService,
    PlanManager planManager,
    TodoManager todoManager,
    ClarifyEngine clarifyEngine,
    ModesService modesService,
    PersonalitiesService personalitiesService,
    MemoryEngine memoryEngine,
    RemoteSessionClient remoteSessionClient,
    CloudExecutionRouter cloudExecutionRouter,
    StartupRegistration startupRegistration,
    TrayIconHost trayIconHost,
    PluginManager pluginManager,
    AutomationEngine automationEngine,
    HistorySearchService historySearchService,
    TelemetryConsentService telemetryConsentService,
    TelemetryQueue telemetryQueue,
    EnvironmentManager environmentManager,
    TerminalManager terminalManager,
    LspManager lspManager,
    McpManager mcpManager,
    SubAgentManager subAgentManager) : BackgroundService
{
    private const int SubscriptionGateRequiredError = -32021;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NexCode helper service listening on pipe {PipeName}", options.Value.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — exit the loop cleanly.
                break;
            }
            catch (Exception ex)
            {
                // A misbehaving client (early disconnect, malformed JSON, dispose-time flush failure, etc.)
                // must NOT take down the helper. Log and resume listening.
                logger.LogWarning(ex, "Pipe connection ended with an unhandled exception; resuming.");
            }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
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
            try
            {
                await writer.WriteLineAsync(responseJson);
            }
            catch (IOException) { /* client went away mid-write — drop the response */ }
            catch (ObjectDisposedException) { /* same */ }
        }
        catch (IOException) { /* peer closed the pipe before we read anything */ }
        catch (ObjectDisposedException) { /* same */ }
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
            IpcMethods.PlanConfirm => await HandlePlanConfirmAsync(request, cancellationToken),
            IpcMethods.PlanReject => await HandlePlanRejectAsync(request, cancellationToken),
            IpcMethods.PlanList => await HandlePlanListAsync(request, cancellationToken),
            IpcMethods.PlanGet => await HandlePlanGetAsync(request, cancellationToken),
            IpcMethods.PlanRequestChanges => await HandlePlanRequestChangesAsync(request, cancellationToken),
            IpcMethods.TodoList => await HandleTodoListAsync(request, cancellationToken),
            IpcMethods.TodoCheckItem => await HandleTodoStatusAsync(request, TodoItemStatus.Done, cancellationToken),
            IpcMethods.TodoUncheckItem => await HandleTodoStatusAsync(request, TodoItemStatus.Pending, cancellationToken),
            IpcMethods.TodoSetInProgress => await HandleTodoStatusAsync(request, TodoItemStatus.InProgress, cancellationToken),
            IpcMethods.TodoSkipItem => await HandleTodoStatusAsync(request, TodoItemStatus.Skipped, cancellationToken),
            IpcMethods.TodoAddItem => await HandleTodoAddItemAsync(request, cancellationToken),
            IpcMethods.TodoDeleteItem => await HandleTodoDeleteItemAsync(request, cancellationToken),
            IpcMethods.TodoReorder => await HandleTodoReorderAsync(request, cancellationToken),
            IpcMethods.ClarifyRespond => await HandleClarifyRespondAsync(request, cancellationToken),
            IpcMethods.ModeList => JsonRpcResponse.Success(request.Id, await modesService.ListAsync(cancellationToken)),
            IpcMethods.ModeUpsert => await HandleModeUpsertAsync(request, cancellationToken),
            IpcMethods.ModeDelete => await HandleModeDeleteAsync(request, cancellationToken),
            IpcMethods.PersonalityList => JsonRpcResponse.Success(request.Id, await personalitiesService.ListAsync(cancellationToken)),
            IpcMethods.PersonalityUpsert => await HandlePersonalityUpsertAsync(request, cancellationToken),
            IpcMethods.PersonalityDelete => await HandlePersonalityDeleteAsync(request, cancellationToken),
            IpcMethods.MemoryList => await HandleMemoryListAsync(request, cancellationToken),
            IpcMethods.MemoryRead => await HandleMemoryReadAsync(request, cancellationToken),
            IpcMethods.MemoryWrite => await HandleMemoryWriteAsync(request, cancellationToken),
            IpcMethods.MemoryDelete => await HandleMemoryDeleteAsync(request, cancellationToken),
            IpcMethods.RemoteConnect => await HandleRemoteConnectAsync(request, cancellationToken),
            IpcMethods.RemoteDisconnect => await HandleRemoteDisconnectAsync(request),
            IpcMethods.RemoteStatus => await HandleRemoteStatusAsync(request, cancellationToken),
            IpcMethods.CloudStatus => await HandleCloudStatusAsync(request, cancellationToken),
            IpcMethods.BackgroundServiceMode => await HandleBackgroundServiceModeAsync(request),
            IpcMethods.PluginList => JsonRpcResponse.Success(request.Id, await pluginManager.ListAsync(cancellationToken)),
            IpcMethods.PluginInstall => await HandlePluginInstallAsync(request, cancellationToken),
            IpcMethods.PluginUninstall => await HandlePluginUninstallAsync(request, cancellationToken),
            IpcMethods.PluginToggle => await HandlePluginToggleAsync(request, cancellationToken),
            IpcMethods.AutomationList => JsonRpcResponse.Success(request.Id, await automationEngine.ListAsync(cancellationToken)),
            IpcMethods.AutomationUpsert => await HandleAutomationUpsertAsync(request, cancellationToken),
            IpcMethods.AutomationRun => await HandleAutomationRunAsync(request, cancellationToken),
            IpcMethods.AutomationDelete => await HandleAutomationDeleteAsync(request, cancellationToken),
            IpcMethods.AutomationToggle => await HandleAutomationToggleAsync(request, cancellationToken),
            IpcMethods.HistoryList => await HandleHistoryListAsync(request, cancellationToken),
            IpcMethods.HistorySearch => await HandleHistorySearchAsync(request, cancellationToken),
            IpcMethods.HistoryExport => await HandleHistoryExportAsync(request, cancellationToken),
            IpcMethods.HistoryArchive => await HandleHistoryArchiveAsync(request, cancellationToken),
            IpcMethods.HistoryDelete => await HandleHistoryDeleteAsync(request, cancellationToken),
            IpcMethods.TelemetryConsent => await HandleTelemetryConsentAsync(request, cancellationToken),
            IpcMethods.TelemetryQueueSize => JsonRpcResponse.Success(request.Id, await telemetryQueue.GetSizeAsync(cancellationToken)),
            IpcMethods.TelemetryClear => JsonRpcResponse.Success(request.Id, new TelemetryClearResponse(await telemetryQueue.ClearAsync(cancellationToken))),
            IpcMethods.EnvironmentList => await HandleEnvironmentListAsync(request, cancellationToken),
            IpcMethods.EnvironmentUpsert => await HandleEnvironmentUpsertAsync(request, cancellationToken),
            IpcMethods.EnvironmentDelete => await HandleEnvironmentDeleteAsync(request, cancellationToken),
            IpcMethods.EditorOpenFile => await HandleEditorOpenFileAsync(request, cancellationToken),
            IpcMethods.EditorSaveFile => await HandleEditorSaveFileAsync(request, cancellationToken),
            IpcMethods.TerminalSpawn => HandleTerminalSpawn(request),
            IpcMethods.TerminalWrite => await HandleTerminalWriteAsync(request),
            IpcMethods.TerminalKill => await HandleTerminalKillAsync(request),
            IpcMethods.LspHover => await HandleLspHoverAsync(request, cancellationToken),
            IpcMethods.LspDiagnostics => await HandleLspDiagnosticsAsync(request, cancellationToken),
            IpcMethods.McpList => JsonRpcResponse.Success(request.Id, await mcpManager.ListAsync(cancellationToken)),
            IpcMethods.McpUpsert => await HandleMcpUpsertAsync(request, cancellationToken),
            IpcMethods.McpRemove => await HandleMcpRemoveAsync(request, cancellationToken),
            IpcMethods.McpConnect => await HandleMcpConnectAsync(request, cancellationToken),
            IpcMethods.McpDisconnect => await HandleMcpDisconnectAsync(request, cancellationToken),
            IpcMethods.McpCallTool => await HandleMcpCallToolAsync(request, cancellationToken),
            IpcMethods.SubAgentSpawn => await HandleSubAgentSpawnAsync(request, cancellationToken),
            IpcMethods.SubAgentKill => await HandleSubAgentKillAsync(request, cancellationToken),
            IpcMethods.SubAgentList => JsonRpcResponse.Success(request.Id, await subAgentManager.ListAsync(cancellationToken)),
            _ => JsonRpcResponse.Failure(request.Id, -32601, $"Unknown method '{request.Method}'.")
        };
    }

    private async Task<JsonRpcResponse> HandlePlanConfirmAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PlanConfirmRequest>();
        if (payload is null || payload.PlanId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plan.confirm payload.");
        }

        try
        {
            var ok = await planManager.ConfirmAsync(payload.PlanId, cancellationToken);
            return JsonRpcResponse.Success(request.Id, new { plan_id = payload.PlanId, confirmed = ok });
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandlePlanRejectAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PlanRejectRequest>();
        if (payload is null || payload.PlanId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plan.reject payload.");
        }

        try
        {
            var ok = await planManager.RejectAsync(payload.PlanId, payload.Reason, cancellationToken);
            return JsonRpcResponse.Success(request.Id, new { plan_id = payload.PlanId, rejected = ok });
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandlePlanListAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PlanListRequest>();
        if (payload is null || payload.SessionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plan.list payload.");
        }

        var response = await planManager.ListForSessionAsync(payload.SessionId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandlePlanGetAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PlanGetRequest>();
        if (payload is null || payload.PlanId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plan.get payload.");
        }

        var detail = await planManager.GetAsync(payload.PlanId, cancellationToken);
        if (detail is null)
        {
            return JsonRpcResponse.Failure(request.Id, -32004, $"Plan '{payload.PlanId}' was not found.");
        }
        return JsonRpcResponse.Success(request.Id, detail);
    }

    private async Task<JsonRpcResponse> HandlePlanRequestChangesAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PlanRequestChangesRequest>();
        if (payload is null || payload.PlanId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Notes))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plan.request_changes payload.");
        }

        var ok = await planManager.RequestChangesAsync(payload.PlanId, payload.Notes, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { plan_id = payload.PlanId, accepted = ok });
    }

    private async Task<JsonRpcResponse> HandleTodoListAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<TodoListRequest>();
        if (payload is null || payload.SessionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid todo.list payload.");
        }

        var lists = await todoManager.ListForSessionAsync(payload.SessionId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new TodoListResponse(payload.SessionId, lists));
    }

    private async Task<JsonRpcResponse> HandleTodoStatusAsync(
        JsonRpcRequest request,
        TodoItemStatus status,
        CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<TodoItemMutationRequest>();
        if (payload is null || payload.ItemId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid todo item mutation payload.");
        }

        var ok = await todoManager.SetItemStatusAsync(payload.ItemId, status, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { item_id = payload.ItemId, status = status.ToString(), ok });
    }

    private async Task<JsonRpcResponse> HandleTodoAddItemAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<TodoAddItemRequest>();
        if (payload is null || payload.ListId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Text))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid todo.add_item payload.");
        }

        var itemId = await todoManager.AddItemAsync(payload.ListId, payload.Text, payload.InsertAfterId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { item_id = itemId, list_id = payload.ListId });
    }

    private async Task<JsonRpcResponse> HandleTodoDeleteItemAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<TodoItemMutationRequest>();
        if (payload is null || payload.ItemId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid todo.delete_item payload.");
        }

        var ok = await todoManager.DeleteItemAsync(payload.ItemId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { item_id = payload.ItemId, deleted = ok });
    }

    private async Task<JsonRpcResponse> HandleTodoReorderAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<TodoReorderRequest>();
        if (payload is null || payload.ListId == Guid.Empty || payload.OrderedItemIds is null)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid todo.reorder payload.");
        }

        var ok = await todoManager.ReorderAsync(payload.ListId, payload.OrderedItemIds, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { list_id = payload.ListId, reordered = ok });
    }

    private async Task<JsonRpcResponse> HandleClarifyRespondAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<ClarifyRespondRequest>();
        if (payload is null || payload.QuestionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid clarify.respond payload.");
        }

        await clarifyEngine.RespondAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { question_id = payload.QuestionId, accepted = true });
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

    private async Task<JsonRpcResponse> HandleModeUpsertAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<ModeUpsertRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mode.upsert payload.");
        }

        try
        {
            var summary = await modesService.UpsertAsync(payload, cancellationToken);
            return JsonRpcResponse.Success(request.Id, summary);
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandleModeDeleteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<ModeDeleteRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mode.delete payload.");
        }

        try
        {
            var deleted = await modesService.DeleteAsync(payload.Id, cancellationToken);
            return JsonRpcResponse.Success(request.Id, new { id = payload.Id, deleted });
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandlePersonalityUpsertAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PersonalityUpsertRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid personality.upsert payload.");
        }

        try
        {
            var summary = await personalitiesService.UpsertAsync(payload, cancellationToken);
            return JsonRpcResponse.Success(request.Id, summary);
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandlePersonalityDeleteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PersonalityDeleteRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid personality.delete payload.");
        }

        try
        {
            var deleted = await personalitiesService.DeleteAsync(payload.Id, cancellationToken);
            return JsonRpcResponse.Success(request.Id, new { id = payload.Id, deleted });
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandleMemoryListAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<MemoryListFilterRequest>() ?? new MemoryListFilterRequest(null, null, null);
        var memories = await memoryEngine.ListAsync(payload.Scope, payload.ProjectId, payload.SessionId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new MemoryListResponse(memories));
    }

    private async Task<JsonRpcResponse> HandleMemoryReadAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<MemoryReadRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Key))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid memory.read payload.");
        }

        var summary = await memoryEngine.ReadAsync(payload.Key, payload.Scope, payload.ProjectId, payload.SessionId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { key = payload.Key, found = summary is not null, memory = summary });
    }

    private async Task<JsonRpcResponse> HandleMemoryWriteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<MemoryWriteRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Key))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid memory.write payload.");
        }

        try
        {
            var summary = await memoryEngine.WriteAsync(
                payload.Key,
                payload.Value ?? string.Empty,
                payload.Scope,
                payload.ProjectId,
                payload.SessionId,
                payload.Tags,
                cancellationToken);
            return JsonRpcResponse.Success(request.Id, summary);
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32020, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandleMemoryDeleteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<MemoryDeleteRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid memory.delete payload.");
        }

        var deleted = await memoryEngine.DeleteAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, deleted });
    }

    private sealed record MemoryListFilterRequest(
        MemoryScope? Scope,
        Guid? ProjectId,
        Guid? SessionId);

    private async Task<JsonRpcResponse> HandlePluginInstallAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PluginInstallRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.SourcePath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plugin.install payload.");
        }

        try
        {
            var summary = await pluginManager.InstallAsync(payload.SourcePath, cancellationToken);
            return JsonRpcResponse.Success(request.Id, summary);
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32030, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandlePluginUninstallAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PluginUninstallRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plugin.uninstall payload.");
        }

        var ok = await pluginManager.UninstallAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, uninstalled = ok });
    }

    private async Task<JsonRpcResponse> HandlePluginToggleAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<PluginToggleRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid plugin.toggle payload.");
        }

        var ok = await pluginManager.ToggleAsync(payload.Id, payload.Enabled, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, enabled = payload.Enabled, ok });
    }

    private async Task<JsonRpcResponse> HandleAutomationUpsertAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<AutomationUpsertRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid automation.upsert payload.");
        }

        var summary = await automationEngine.UpsertAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, summary);
    }

    private async Task<JsonRpcResponse> HandleAutomationRunAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<AutomationRunRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid automation.run payload.");
        }

        var ok = await automationEngine.RunAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, started = ok });
    }

    private async Task<JsonRpcResponse> HandleAutomationDeleteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<AutomationDeleteRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid automation.delete payload.");
        }

        var ok = await automationEngine.DeleteAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, deleted = ok });
    }

    private async Task<JsonRpcResponse> HandleAutomationToggleAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<AutomationToggleRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid automation.toggle payload.");
        }

        var ok = await automationEngine.ToggleAsync(payload.Id, payload.Enabled, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, enabled = payload.Enabled, ok });
    }

    private async Task<JsonRpcResponse> HandleHistoryListAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<HistoryListRequest>();
        var response = await historySearchService.ListAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleHistorySearchAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<HistorySearchRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Query))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid history.search payload.");
        }

        var response = await historySearchService.SearchAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleHistoryExportAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<HistoryExportRequest>();
        if (payload is null || payload.SessionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid history.export payload.");
        }

        var response = await historySearchService.ExportAsync(payload.SessionId, payload.Format ?? "json", cancellationToken);
        if (response is null)
        {
            return JsonRpcResponse.Failure(request.Id, -32004, $"Session '{payload.SessionId}' was not found.");
        }
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleHistoryArchiveAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<HistoryArchiveRequest>();
        if (payload is null || payload.SessionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid history.archive payload.");
        }

        var ok = await historySearchService.ArchiveAsync(payload.SessionId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { session_id = payload.SessionId, archived = ok });
    }

    private async Task<JsonRpcResponse> HandleHistoryDeleteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<HistoryDeleteRequest>();
        if (payload is null || payload.SessionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid history.delete payload.");
        }

        var ok = await historySearchService.DeleteAsync(payload.SessionId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { session_id = payload.SessionId, deleted = ok });
    }

    private async Task<JsonRpcResponse> HandleTelemetryConsentAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<TelemetryConsentRequest>();
        if (payload is null)
        {
            var current = await telemetryConsentService.GetAsync(cancellationToken);
            var size = await telemetryQueue.GetSizeAsync(cancellationToken);
            return JsonRpcResponse.Success(request.Id, new TelemetryConsentResponse(current, size.QueuedEvents));
        }

        var enabled = await telemetryConsentService.SetAsync(payload.Enabled, cancellationToken);
        var queueSize = await telemetryQueue.GetSizeAsync(cancellationToken);
        return JsonRpcResponse.Success(request.Id, new TelemetryConsentResponse(enabled, queueSize.QueuedEvents));
    }

    private async Task<JsonRpcResponse> HandleEnvironmentListAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<EnvironmentListRequest>();
        var response = await environmentManager.ListAsync(payload?.ProjectId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleEnvironmentUpsertAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<EnvironmentUpsertRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid environment.upsert payload.");
        }

        var response = await environmentManager.UpsertAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleEnvironmentDeleteAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<EnvironmentDeleteRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid environment.delete payload.");
        }

        var ok = await environmentManager.DeleteAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, deleted = ok });
    }

    private async Task<JsonRpcResponse> HandleRemoteConnectAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<RemoteConnectRequest>() ?? new RemoteConnectRequest(null, null);
        try
        {
            var health = await remoteSessionClient.CheckHealthAsync(cancellationToken);
            return JsonRpcResponse.Success(
                request.Id,
                new RemoteConnectResponse(true, payload.Endpoint ?? remoteSessionClient.Endpoint, health.Message));
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Success(
                request.Id,
                new RemoteConnectResponse(false, payload.Endpoint ?? remoteSessionClient.Endpoint, ex.Message));
        }
    }

    private async Task<JsonRpcResponse> HandleRemoteDisconnectAsync(JsonRpcRequest request)
    {
        await remoteSessionClient.DisconnectAsync();
        return JsonRpcResponse.Success(request.Id, new { disconnected = true });
    }

    private async Task<JsonRpcResponse> HandleRemoteStatusAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!remoteSessionClient.IsConnected)
        {
            return JsonRpcResponse.Success(
                request.Id,
                new RemoteStatusResponse(false, remoteSessionClient.Endpoint, null, "disconnected"));
        }

        try
        {
            var health = await remoteSessionClient.CheckHealthAsync(cancellationToken);
            return JsonRpcResponse.Success(
                request.Id,
                new RemoteStatusResponse(true, remoteSessionClient.Endpoint, health.Version, health.State));
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Success(
                request.Id,
                new RemoteStatusResponse(false, remoteSessionClient.Endpoint, null, $"error:{ex.Message}"));
        }
    }

    private async Task<JsonRpcResponse> HandleCloudStatusAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        DateTimeOffset? lastSync = null;
        var payload = request.DeserializeParams<CloudStatusFilterRequest>();
        if (payload is { ProjectRoot: { Length: > 0 } projectRoot })
        {
            lastSync = await cloudExecutionRouter.GetLastSyncAsync(projectRoot);
        }

        return JsonRpcResponse.Success(
            request.Id,
            new CloudStatusResponse(
                cloudExecutionRouter.IsConfigured && remoteSessionClient.IsConnected,
                cloudExecutionRouter.CloudEndpoint,
                lastSync));
    }

    private async Task<JsonRpcResponse> HandleBackgroundServiceModeAsync(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<BackgroundServiceModeRequest>();
        if (payload is null)
        {
            var current = await startupRegistration.IsEnabledAsync();
            return JsonRpcResponse.Success(request.Id, new BackgroundServiceModeResponse(current, current));
        }

        if (payload.Enabled)
        {
            var exe = ResolveGuiExecutablePath();
            await startupRegistration.EnableAsync(exe);
            trayIconHost.SetState(TrayState.Background);
        }
        else
        {
            await startupRegistration.DisableAsync();
            trayIconHost.SetState(TrayState.Idle);
        }

        var enabled = await startupRegistration.IsEnabledAsync();
        return JsonRpcResponse.Success(request.Id, new BackgroundServiceModeResponse(payload.Enabled, enabled));
    }

    private static string ResolveGuiExecutablePath()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "NexCode.Gui.exe");
        if (File.Exists(probe))
        {
            return probe;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(programFiles, "NexCode", "NexCode.Gui.exe");
    }

    private sealed record CloudStatusFilterRequest(string? ProjectRoot);

    // ----- Slice 0015: editor / terminal / LSP handlers -----

    private async Task<JsonRpcResponse> HandleEditorOpenFileAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<EditorOpenFileRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProjectPath) || string.IsNullOrWhiteSpace(payload.FilePath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid editor.open_file payload.");
        }

        if (!PathSafety.EnsureWithinRoot(payload.ProjectPath, payload.FilePath, out var fullPath))
        {
            return JsonRpcResponse.Success(request.Id, new EditorOpenFileResponse(
                payload.ProjectPath, payload.FilePath, GuessLanguage(payload.FilePath), string.Empty, 0, false, "path_outside_root"));
        }

        if (!File.Exists(fullPath))
        {
            return JsonRpcResponse.Success(request.Id, new EditorOpenFileResponse(
                payload.ProjectPath, fullPath, GuessLanguage(fullPath), string.Empty, 0, false, "file_not_found"));
        }

        var info = new FileInfo(fullPath);
        const int budget = 1_000_000;
        var truncated = info.Length > budget;
        var contents = truncated
            ? string.Empty
            : await File.ReadAllTextAsync(fullPath, cancellationToken);
        if (truncated)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[budget];
            var read = await stream.ReadAsync(buffer.AsMemory(0, budget), cancellationToken);
            contents = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }

        return JsonRpcResponse.Success(request.Id, new EditorOpenFileResponse(
            payload.ProjectPath, fullPath, GuessLanguage(fullPath), contents, info.Length, truncated, null));
    }

    private async Task<JsonRpcResponse> HandleEditorSaveFileAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<EditorSaveFileRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProjectPath) || string.IsNullOrWhiteSpace(payload.FilePath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid editor.save_file payload.");
        }

        if (!PathSafety.EnsureWithinRoot(payload.ProjectPath, payload.FilePath, out var fullPath))
        {
            return JsonRpcResponse.Success(request.Id, new EditorSaveFileResponse(
                payload.ProjectPath, payload.FilePath, false, "path_outside_root"));
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, payload.Content ?? string.Empty, cancellationToken);
            serviceEventHub.Publish(
                ServiceEventTypes.FileChanged,
                new FileChangedEventPayload(fullPath, "saved"));
            return JsonRpcResponse.Success(request.Id, new EditorSaveFileResponse(payload.ProjectPath, fullPath, true, null));
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Success(request.Id, new EditorSaveFileResponse(payload.ProjectPath, fullPath, false, ex.Message));
        }
    }

    private JsonRpcResponse HandleTerminalSpawn(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<TerminalSpawnRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProjectPath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid terminal.spawn payload.");
        }

        var response = terminalManager.Spawn(payload);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleTerminalWriteAsync(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<TerminalWriteRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.TerminalId))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid terminal.write payload.");
        }

        var response = await terminalManager.WriteAsync(payload);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleTerminalKillAsync(JsonRpcRequest request)
    {
        var payload = request.DeserializeParams<TerminalKillRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.TerminalId))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid terminal.kill payload.");
        }

        var response = await terminalManager.KillAsync(payload);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleLspHoverAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<LspHoverRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.FilePath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid lsp.hover payload.");
        }

        var response = await lspManager.HoverAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleLspDiagnosticsAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<LspDiagnosticsRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.FilePath))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid lsp.diagnostics payload.");
        }

        var response = await lspManager.DiagnosticsAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private static string GuessLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
            ".json" => "json",
            ".md" => "markdown",
            ".py" => "python",
            ".html" => "html",
            ".css" => "css",
            ".xaml" or ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".rs" => "rust",
            ".go" => "go",
            ".java" => "java",
            ".kt" => "kotlin",
            ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "cpp",
            ".sh" => "shell",
            ".ps1" => "powershell",
            _ => "plaintext"
        };
    }

    private async Task<JsonRpcResponse> HandleMcpUpsertAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<McpUpsertRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mcp.upsert payload.");
        }
        var response = await mcpManager.UpsertAsync(payload, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleMcpRemoveAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<McpRemoveRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mcp.remove payload.");
        }
        var ok = await mcpManager.RemoveAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, removed = ok });
    }

    private async Task<JsonRpcResponse> HandleMcpConnectAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<McpConnectRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mcp.connect payload.");
        }
        try
        {
            await mcpManager.ConnectAsync(payload.Id, cancellationToken);
            return JsonRpcResponse.Success(request.Id, new { id = payload.Id, connected = true });
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Failure(request.Id, -32030, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandleMcpDisconnectAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<McpDisconnectRequest>();
        if (payload is null || payload.Id == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mcp.disconnect payload.");
        }
        var ok = await mcpManager.DisconnectAsync(payload.Id, cancellationToken);
        return JsonRpcResponse.Success(request.Id, new { id = payload.Id, disconnected = ok });
    }

    private async Task<JsonRpcResponse> HandleMcpCallToolAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<McpCallToolRequest>();
        if (payload is null || payload.ServerId == Guid.Empty || string.IsNullOrWhiteSpace(payload.ToolName))
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid mcp.call_tool payload.");
        }
        var response = await mcpManager.CallToolAsync(payload.ServerId, payload.ToolName, payload.ArgumentsJson, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }

    private async Task<JsonRpcResponse> HandleSubAgentSpawnAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<SubAgentSpawnRequest>();
        if (payload is null || payload.ParentSessionId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid subagent.spawn payload.");
        }

        var snapshot = await accountStateService.GetSnapshotAsync(cancellationToken);
        try
        {
            var permissionContext = sessionRegistry.TryGet(payload.ParentSessionId, out var session) && session is not null
                ? new SubAgentManager.PermissionLevelDescriptor(session.Request.PermissionLevel, session.Request.SandboxEnabled)
                : new SubAgentManager.PermissionLevelDescriptor(PermissionLevel.Default, SandboxEnabled: false);

            var response = await subAgentManager.SpawnAsync(payload, snapshot.Capabilities, permissionContext, cancellationToken);
            return JsonRpcResponse.Success(request.Id, response);
        }
        catch (InvalidOperationException ex)
        {
            return JsonRpcResponse.Failure(request.Id, SubscriptionGateRequiredError, ex.Message);
        }
    }

    private async Task<JsonRpcResponse> HandleSubAgentKillAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = request.DeserializeParams<SubAgentKillRequest>();
        if (payload is null || payload.SubAgentId == Guid.Empty)
        {
            return JsonRpcResponse.Failure(request.Id, -32602, "Invalid subagent.kill payload.");
        }
        var response = await subAgentManager.KillAsync(payload.SubAgentId, cancellationToken);
        return JsonRpcResponse.Success(request.Id, response);
    }
}
