using System.IO.Pipes;
using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

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

    // ----- Sessions -----
    public Task CancelSessionAsync(Guid sessionId, string? reason = null, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.SessionCancel, new SessionCancelRequest(sessionId, reason), cancellationToken);

    // ----- Memory -----
    public Task<MemoryListResponse> ListMemoriesAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<MemoryListResponse>(IpcMethods.MemoryList, new { }, cancellationToken);

    public Task<MemorySummary> WriteMemoryAsync(string key, string value, MemoryScope scope, Guid? projectId = null, Guid? sessionId = null, string[]? tags = null, CancellationToken cancellationToken = default)
        => SendRequestAsync<MemorySummary>(IpcMethods.MemoryWrite, new MemoryWriteRequest(key, value, scope, projectId, sessionId, tags), cancellationToken);

    public Task DeleteMemoryAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.MemoryDelete, new MemoryDeleteRequest(id), cancellationToken);

    // ----- History -----
    public Task<HistoryListResponse> ListHistoryAsync(int? limit = null, string? projectFilter = null, CancellationToken cancellationToken = default)
        => SendRequestAsync<HistoryListResponse>(IpcMethods.HistoryList, new HistoryListRequest(limit, projectFilter), cancellationToken);

    public Task<HistorySearchResponse> SearchHistoryAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
        => SendRequestAsync<HistorySearchResponse>(IpcMethods.HistorySearch, new HistorySearchRequest(query, null, null, null, null, null, limit), cancellationToken);

    public Task<HistoryExportResponse> ExportHistoryAsync(Guid sessionId, string format = "json", CancellationToken cancellationToken = default)
        => SendRequestAsync<HistoryExportResponse>(IpcMethods.HistoryExport, new HistoryExportRequest(sessionId, format), cancellationToken);

    public Task ArchiveHistoryAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.HistoryArchive, new HistoryArchiveRequest(sessionId), cancellationToken);

    public Task DeleteHistoryAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.HistoryDelete, new HistoryDeleteRequest(sessionId), cancellationToken);

    // ----- Plans / Todos -----
    public Task<PlanListResponse> ListPlansAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => SendRequestAsync<PlanListResponse>(IpcMethods.PlanList, new PlanListRequest(sessionId), cancellationToken);

    public Task<PlanDetail> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default)
        => SendRequestAsync<PlanDetail>(IpcMethods.PlanGet, new PlanGetRequest(planId), cancellationToken);

    public Task ConfirmPlanAsync(Guid planId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.PlanConfirm, new PlanConfirmRequest(planId), cancellationToken);

    public Task RejectPlanAsync(Guid planId, string? reason = null, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.PlanReject, new PlanRejectRequest(planId, reason), cancellationToken);

    public Task RequestPlanChangesAsync(Guid planId, string notes, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.PlanRequestChanges, new PlanRequestChangesRequest(planId, notes), cancellationToken);

    public Task<TodoListResponse> ListTodosAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => SendRequestAsync<TodoListResponse>(IpcMethods.TodoList, new TodoListRequest(sessionId), cancellationToken);

    public Task CheckTodoItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoCheckItem, new TodoItemMutationRequest(itemId), cancellationToken);

    public Task UncheckTodoItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoUncheckItem, new TodoItemMutationRequest(itemId), cancellationToken);

    public Task SetTodoInProgressAsync(Guid itemId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoSetInProgress, new TodoItemMutationRequest(itemId), cancellationToken);

    public Task SkipTodoItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoSkipItem, new TodoItemMutationRequest(itemId), cancellationToken);

    public Task AddTodoItemAsync(Guid listId, string text, Guid? insertAfterId = null, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoAddItem, new TodoAddItemRequest(listId, text, insertAfterId), cancellationToken);

    public Task DeleteTodoItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoDeleteItem, new TodoItemMutationRequest(itemId), cancellationToken);

    public Task ReorderTodosAsync(Guid listId, Guid[] orderedItemIds, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.TodoReorder, new TodoReorderRequest(listId, orderedItemIds), cancellationToken);

    // ----- Git -----
    public Task<GitStatusResponse> GetGitStatusAsync(string projectPath, CancellationToken cancellationToken = default)
        => SendRequestAsync<GitStatusResponse>(IpcMethods.GitStatus, new GitStatusRequest(projectPath), cancellationToken);

    public Task<GitDiffResponse> GetGitDiffAsync(string projectPath, string? fromRef = null, string? toRef = null, CancellationToken cancellationToken = default)
        => SendRequestAsync<GitDiffResponse>(IpcMethods.GitDiff, new GitDiffRequest(projectPath, fromRef, toRef), cancellationToken);

    public Task<GitRevertResponse> RevertToCheckpointAsync(string projectPath, string checkpointCommitHash, CancellationToken cancellationToken = default)
        => SendRequestAsync<GitRevertResponse>(IpcMethods.GitRevert, new GitRevertRequest(projectPath, checkpointCommitHash), cancellationToken);

    // ----- Modes -----
    public Task<ModeListResponse> ListModesAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<ModeListResponse>(IpcMethods.ModeList, new { }, cancellationToken);

    public Task<ModeSummary> UpsertModeAsync(ModeUpsertRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<ModeSummary>(IpcMethods.ModeUpsert, request, cancellationToken);

    public Task DeleteModeAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.ModeDelete, new ModeDeleteRequest(id), cancellationToken);

    // ----- Personalities -----
    public Task<PersonalityListResponse> ListPersonalitiesAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<PersonalityListResponse>(IpcMethods.PersonalityList, new { }, cancellationToken);

    public Task<PersonalitySummary> UpsertPersonalityAsync(PersonalityUpsertRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<PersonalitySummary>(IpcMethods.PersonalityUpsert, request, cancellationToken);

    public Task DeletePersonalityAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.PersonalityDelete, new PersonalityDeleteRequest(id), cancellationToken);

    // ----- MCP servers -----
    public Task<McpListResponse> ListMcpServersAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<McpListResponse>(IpcMethods.McpList, new { }, cancellationToken);

    public Task<McpUpsertResponse> UpsertMcpServerAsync(McpUpsertRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<McpUpsertResponse>(IpcMethods.McpUpsert, request, cancellationToken);

    public Task RemoveMcpServerAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.McpRemove, new McpRemoveRequest(id), cancellationToken);

    public Task ConnectMcpServerAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.McpConnect, new McpConnectRequest(id), cancellationToken);

    public Task DisconnectMcpServerAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.McpDisconnect, new McpDisconnectRequest(id), cancellationToken);

    public Task<McpCallToolResponse> CallMcpToolAsync(McpCallToolRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<McpCallToolResponse>(IpcMethods.McpCallTool, request, cancellationToken);

    // ----- Environments -----
    public Task<EnvironmentListResponse> ListEnvironmentsAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
        => SendRequestAsync<EnvironmentListResponse>(IpcMethods.EnvironmentList, new EnvironmentListRequest(projectId), cancellationToken);

    public Task<EnvironmentUpsertResponse> UpsertEnvironmentAsync(EnvironmentUpsertRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<EnvironmentUpsertResponse>(IpcMethods.EnvironmentUpsert, request, cancellationToken);

    public Task DeleteEnvironmentAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.EnvironmentDelete, new EnvironmentDeleteRequest(id), cancellationToken);

    // ----- Telemetry / Privacy -----
    public Task<TelemetryConsentResponse> GetTelemetryConsentAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<TelemetryConsentResponse>(IpcMethods.TelemetryConsent, new { }, cancellationToken);

    public Task<TelemetryConsentResponse> SetTelemetryConsentAsync(bool enabled, CancellationToken cancellationToken = default)
        => SendRequestAsync<TelemetryConsentResponse>(IpcMethods.TelemetryConsent, new TelemetryConsentRequest(enabled), cancellationToken);

    public Task<TelemetryClearResponse> ClearTelemetryAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<TelemetryClearResponse>(IpcMethods.TelemetryClear, new { }, cancellationToken);

    // ----- Plugins -----
    public Task<PluginListResponse> ListPluginsAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<PluginListResponse>(IpcMethods.PluginList, new { }, cancellationToken);

    public Task<PluginSummary> InstallPluginAsync(string sourcePath, CancellationToken cancellationToken = default)
        => SendRequestAsync<PluginSummary>(IpcMethods.PluginInstall, new PluginInstallRequest(sourcePath), cancellationToken);

    public Task UninstallPluginAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.PluginUninstall, new PluginUninstallRequest(id), cancellationToken);

    public Task TogglePluginAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.PluginToggle, new PluginToggleRequest(id, enabled), cancellationToken);

    // ----- Automations -----
    public Task<AutomationListResponse> ListAutomationsAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<AutomationListResponse>(IpcMethods.AutomationList, new { }, cancellationToken);

    public Task<AutomationSummary> UpsertAutomationAsync(AutomationUpsertRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<AutomationSummary>(IpcMethods.AutomationUpsert, request, cancellationToken);

    public Task RunAutomationAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.AutomationRun, new AutomationRunRequest(id), cancellationToken);

    public Task DeleteAutomationAsync(Guid id, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.AutomationDelete, new AutomationDeleteRequest(id), cancellationToken);

    public Task ToggleAutomationAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.AutomationToggle, new AutomationToggleRequest(id, enabled), cancellationToken);

    // ----- Sub-agents -----
    public Task<SubAgentListResponse> ListSubAgentsAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<SubAgentListResponse>(IpcMethods.SubAgentList, new { }, cancellationToken);

    public Task<SubAgentSpawnResponse> SpawnSubAgentAsync(Guid parentSessionId, string configJson, CancellationToken cancellationToken = default)
        => SendRequestAsync<SubAgentSpawnResponse>(IpcMethods.SubAgentSpawn, new SubAgentSpawnRequest(parentSessionId, configJson), cancellationToken);

    public Task<SubAgentKillResponse> KillSubAgentAsync(Guid subAgentId, CancellationToken cancellationToken = default)
        => SendRequestAsync<SubAgentKillResponse>(IpcMethods.SubAgentKill, new SubAgentKillRequest(subAgentId), cancellationToken);

    // ----- Editor -----
    public Task<EditorOpenFileResponse> OpenFileAsync(string projectPath, string filePath, CancellationToken cancellationToken = default)
        => SendRequestAsync<EditorOpenFileResponse>(IpcMethods.EditorOpenFile, new EditorOpenFileRequest(projectPath, filePath), cancellationToken);

    public Task<EditorSaveFileResponse> SaveFileAsync(string projectPath, string filePath, string content, CancellationToken cancellationToken = default)
        => SendRequestAsync<EditorSaveFileResponse>(IpcMethods.EditorSaveFile, new EditorSaveFileRequest(projectPath, filePath, content), cancellationToken);

    // ----- Terminal -----
    public Task<TerminalSpawnResponse> SpawnTerminalAsync(string projectPath, string? shell, int cols, int rows, CancellationToken cancellationToken = default)
        => SendRequestAsync<TerminalSpawnResponse>(IpcMethods.TerminalSpawn, new TerminalSpawnRequest(projectPath, shell, null, cols, rows, null), cancellationToken);

    public Task<TerminalWriteResponse> WriteTerminalAsync(string terminalId, string dataBase64, int? cols = null, int? rows = null, CancellationToken cancellationToken = default)
        => SendRequestAsync<TerminalWriteResponse>(IpcMethods.TerminalWrite, new TerminalWriteRequest(terminalId, dataBase64, cols, rows), cancellationToken);

    public Task<TerminalKillResponse> KillTerminalAsync(string terminalId, CancellationToken cancellationToken = default)
        => SendRequestAsync<TerminalKillResponse>(IpcMethods.TerminalKill, new TerminalKillRequest(terminalId), cancellationToken);

    // ----- Remote / Cloud -----
    public Task<RemoteStatusResponse> GetRemoteStatusAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RemoteStatusResponse>(IpcMethods.RemoteStatus, new { }, cancellationToken);

    public Task<RemoteConnectResponse> ConnectRemoteAsync(string? endpoint = null, string? bearerToken = null, CancellationToken cancellationToken = default)
        => SendRequestAsync<RemoteConnectResponse>(IpcMethods.RemoteConnect, new RemoteConnectRequest(endpoint, bearerToken), cancellationToken);

    public Task DisconnectRemoteAsync(CancellationToken cancellationToken = default)
        => SendCommandAsync(IpcMethods.RemoteDisconnect, new { }, cancellationToken);

    public Task<CloudStatusResponse> GetCloudStatusAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<CloudStatusResponse>(IpcMethods.CloudStatus, new { }, cancellationToken);

    /// <summary>Sends a request whose response body is not needed; throws on a JSON-RPC error.</summary>
    private static async Task SendCommandAsync(string method, object payload, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(cancellationToken);
        using var reader = new StreamReader(client);
        await using var writer = new StreamWriter(client) { AutoFlush = true };

        var request = JsonRpcRequest.Create(method, payload);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonSerialization.Options));

        var responseJson = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("The helper service returned an empty payload.");
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("The helper service response was invalid.");
        if (response.Error is not null)
        {
            throw new HelperRequestException(response.Error.Code, response.Error.Message);
        }
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
