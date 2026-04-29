namespace NexCode.Shared.Ipc;

public static class IpcMethods
{
    public const string ServiceHealth = "service.health";
    public const string ServicePollEvents = "service.poll_events";
    public const string AccountGetSnapshot = "account.get_snapshot";
    public const string AccountSignIn = "account.sign_in";
    public const string AccountRefreshSubscription = "account.refresh_subscription";
    public const string SessionCreate = "session.create";
    public const string SessionSendMessage = "session.send_message";
    public const string SessionCancel = "session.cancel";
    public const string PlanConfirm = "plan.confirm";
    public const string PlanReject = "plan.reject";

    // Slice 0011
    public const string ProviderList = "provider.list";
    public const string ProviderUpsert = "provider.upsert";
    public const string ProviderRemove = "provider.remove";
    public const string ProviderSetDefault = "provider.set_default";
    public const string PermissionRespond = "permission.respond";
    public const string GitStatus = "git.status";
    public const string GitDiff = "git.diff";
    public const string GitRevert = "git.revert";

    // Slice 0013 — plan/todo/clarify
    public const string PlanList = "plan.list";
    public const string PlanGet = "plan.get";
    public const string PlanRequestChanges = "plan.request_changes";
    public const string TodoList = "todo.list";
    public const string TodoCheckItem = "todo.check_item";
    public const string TodoUncheckItem = "todo.uncheck_item";
    public const string TodoSetInProgress = "todo.set_in_progress";
    public const string TodoSkipItem = "todo.skip_item";
    public const string TodoAddItem = "todo.add_item";
    public const string TodoDeleteItem = "todo.delete_item";
    public const string TodoReorder = "todo.reorder";
    public const string ClarifyRespond = "clarify.respond";

    // Slice 0014 — modes / personalities / memory / sandbox
    public const string ModeList = "mode.list";
    public const string ModeUpsert = "mode.upsert";
    public const string ModeDelete = "mode.delete";
    public const string PersonalityList = "personality.list";
    public const string PersonalityUpsert = "personality.upsert";
    public const string PersonalityDelete = "personality.delete";
    public const string MemoryList = "memory.list";
    public const string MemoryRead = "memory.read";
    public const string MemoryWrite = "memory.write";
    public const string MemoryDelete = "memory.delete";

    // Slice 0015 — editor / terminal / lsp / git
    public const string EditorOpenFile = "editor.open_file";
    public const string EditorSaveFile = "editor.save_file";
    public const string TerminalSpawn = "terminal.spawn";
    public const string TerminalWrite = "terminal.write";
    public const string TerminalKill = "terminal.kill";
    public const string LspHover = "lsp.hover";
    public const string LspDiagnostics = "lsp.diagnostics";

    // Slice 0016 — mcp / sub-agents
    public const string McpList = "mcp.list";
    public const string McpUpsert = "mcp.upsert";
    public const string McpRemove = "mcp.remove";
    public const string McpConnect = "mcp.connect";
    public const string McpDisconnect = "mcp.disconnect";
    public const string McpCallTool = "mcp.call_tool";
    public const string SubAgentSpawn = "subagent.spawn";
    public const string SubAgentKill = "subagent.kill";
    public const string SubAgentList = "subagent.list";

    // Slice 0017 — plugins / automations / history / telemetry / settings
    public const string PluginList = "plugin.list";
    public const string PluginInstall = "plugin.install";
    public const string PluginUninstall = "plugin.uninstall";
    public const string PluginToggle = "plugin.toggle";
    public const string AutomationList = "automation.list";
    public const string AutomationUpsert = "automation.upsert";
    public const string AutomationRun = "automation.run";
    public const string AutomationDelete = "automation.delete";
    public const string AutomationToggle = "automation.toggle";
    public const string HistoryList = "history.list";
    public const string HistorySearch = "history.search";
    public const string HistoryExport = "history.export";
    public const string HistoryArchive = "history.archive";
    public const string HistoryDelete = "history.delete";
    public const string TelemetryConsent = "telemetry.consent";
    public const string TelemetryQueueSize = "telemetry.queue_size";
    public const string TelemetryClear = "telemetry.clear";
    public const string EnvironmentList = "environment.list";
    public const string EnvironmentUpsert = "environment.upsert";
    public const string EnvironmentDelete = "environment.delete";
    public const string ThemeList = "theme.list";
    public const string ThemeUpsert = "theme.upsert";
    public const string KeyBindingList = "keybinding.list";
    public const string KeyBindingUpsert = "keybinding.upsert";
    public const string KeyBindingReset = "keybinding.reset";

    // Slice 0018 — remote / cloud / background-service / store
    public const string RemoteConnect = "remote.connect";
    public const string RemoteDisconnect = "remote.disconnect";
    public const string RemoteStatus = "remote.status";
    public const string CloudStatus = "cloud.status";
    public const string BackgroundServiceMode = "service.background_mode";
}
