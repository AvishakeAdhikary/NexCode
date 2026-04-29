using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using NexCode.Data.Storage;
using NexCode.Data.Extensions;
using NexCode.Service.Auth;
using NexCode.Service.Automations;
using NexCode.Service.BackgroundServiceMode;
using NexCode.Service.Cloud;
using NexCode.Service.Environments;
using NexCode.Service.Git;
using NexCode.Service.History;
using NexCode.Service.Lsp;
using NexCode.Service.Marketplace;
using NexCode.Service.Mcp;
using NexCode.Service.Memory;
using NexCode.Service.Modes;
using NexCode.Service.Permissions;
using NexCode.Service.Personalities;
using NexCode.Service.Plans;
using NexCode.Service.Plugins;
using NexCode.Service.Providers;
using NexCode.Service.Remote;
using NexCode.Service.Sandbox;
using NexCode.Service.SubAgents;
using NexCode.Service.Sessions;
using NexCode.Service.Telemetry;
using NexCode.Service.Terminal;
using NexCode.Service.Tools;
using NexCode.Service.Tools.Implementations;
using NexCode.Data.Repositories;

namespace NexCode.Service;

[SupportedOSPlatform("windows10.0.17763.0")]
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Services.Configure<ServiceHostOptions>(
            builder.Configuration.GetSection(ServiceHostOptions.SectionName));
        builder.Services.Configure<AuthOptions>(
            builder.Configuration.GetSection(AuthOptions.SectionName));
        builder.Services.PostConfigure<AuthOptions>(options =>
        {
            options.ClientId = FirstNonEmpty(
                Environment.GetEnvironmentVariable("NEXCODE_AAD_CLIENT_ID"),
                options.ClientId);
            options.TenantId = FirstNonEmpty(
                Environment.GetEnvironmentVariable("NEXCODE_AAD_TENANT_ID"),
                options.TenantId);
        });
        builder.Services.Configure<IapOptions>(
            builder.Configuration.GetSection(IapOptions.SectionName));
        builder.Services.Configure<SuperUserGrantOptions>(
            builder.Configuration.GetSection(SuperUserGrantOptions.SectionName));

        // Core helper services
        builder.Services.AddSingleton<SessionRegistry>();
        builder.Services.AddSingleton<ServiceEventHub>();
        builder.Services.AddSingleton<AccountStateService>();
        builder.Services.AddSingleton<SessionTurnService>();

        // Auth + Store
        builder.Services.AddSingleton<MsalTokenCacheStore>();
        builder.Services.AddSingleton<IMsalAuthService, MsalAuthService>();
        builder.Services.AddSingleton<ISuperUserGrantService, SuperUserGrantService>();
        builder.Services.AddSingleton<IStoreSubscriptionService, WindowsStoreSubscriptionService>();

        // Slice 0011: provider abstraction + real LLM adapters
        builder.Services.AddHttpClient(AnthropicMessagesProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        builder.Services.AddHttpClient(OpenAIResponsesProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // Slice 0016: additional provider adapters
        builder.Services.AddHttpClient(GeminiProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(BedrockProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(AzureOpenAiProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(GroqProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(OpenRouterProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(OllamaProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(10));
        builder.Services.AddHttpClient(LmStudioProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(10));
        builder.Services.AddHttpClient(CustomOpenAiProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(StreamableHttpMcpTransport.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddHttpClient(MarketplaceFixtureService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddHttpClient(NexCode.Service.Mcp.Auth.McpOauthPkceClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));

        builder.Services.AddSingleton<IModelProvider, AnthropicMessagesProvider>();
        builder.Services.AddSingleton<IModelProvider, OpenAIResponsesProvider>();
        builder.Services.AddSingleton<IModelProvider, GeminiProvider>();
        builder.Services.AddSingleton<IModelProvider, BedrockProvider>();
        builder.Services.AddSingleton<IModelProvider, AzureOpenAiProvider>();
        builder.Services.AddSingleton<IModelProvider, GroqProvider>();
        builder.Services.AddSingleton<IModelProvider, OpenRouterProvider>();
        builder.Services.AddSingleton<IModelProvider, OllamaProvider>();
        builder.Services.AddSingleton<IModelProvider, LmStudioProvider>();
        builder.Services.AddSingleton<IModelProvider, CustomOpenAiProvider>();
        builder.Services.AddSingleton<IModelProviderRegistry, ModelProviderRegistry>();
        builder.Services.AddSingleton<ProviderApiKeyProtector>();
        builder.Services.AddSingleton<ProviderConfigurationService>();

        // Slice 0011: tool registry + built-in tools
        builder.Services.AddSingleton<ITool, ReadFileTool>();
        builder.Services.AddSingleton<ITool, WriteFileTool>();
        builder.Services.AddSingleton<ITool, CreateFileTool>();
        builder.Services.AddSingleton<ITool, DeleteFileTool>();
        builder.Services.AddSingleton<ITool, ListDirectoryTool>();
        builder.Services.AddSingleton<ITool, SearchFilesTool>();
        builder.Services.AddSingleton<ITool, ExecuteCommandTool>();
        builder.Services.AddSingleton<ITool, CutPasteFileTool>();
        builder.Services.AddSingleton<ITool, GitStatusTool>();
        builder.Services.AddSingleton<ITool, GitDiffTool>();
        builder.Services.AddSingleton<ITool, GitRevertTool>();

        // Slice 0013 — plan / todo / clarify managers + tools
        builder.Services.AddSingleton<PlanManager>();
        builder.Services.AddSingleton<TodoManager>();
        builder.Services.AddSingleton<ClarifyEngine>();
        builder.Services.AddSingleton<ITool, CreatePlanTool>();
        builder.Services.AddSingleton<ITool, UpdatePlanTool>();
        builder.Services.AddSingleton<ITool, DeletePlanTool>();
        builder.Services.AddSingleton<ITool, CreateTodoListTool>();
        builder.Services.AddSingleton<ITool, AddTodoItemTool>();
        builder.Services.AddSingleton<ITool, CheckTodoItemTool>();
        builder.Services.AddSingleton<ITool, UncheckTodoItemTool>();
        builder.Services.AddSingleton<ITool, SetTodoItemInProgressTool>();
        builder.Services.AddSingleton<ITool, SkipTodoItemTool>();
        builder.Services.AddSingleton<ITool, DeleteTodoItemTool>();
        builder.Services.AddSingleton<ITool, DeleteTodoListTool>();
        builder.Services.AddSingleton<ITool, ClarifyQuestionTool>();

        // Slice 0014 — modes / personalities / memory / sandbox
        builder.Services.AddSingleton<ModesService>();
        builder.Services.AddSingleton<PersonalitiesService>();
        builder.Services.AddSingleton<SessionMemoryStore>();
        builder.Services.AddSingleton<MemoryEngine>();
        builder.Services.AddSingleton<SandboxService>();
        builder.Services.AddTransient<WindowsJobObject>(_ => new WindowsJobObject(WindowsJobObject.JobLimits.Default));
        builder.Services.AddSingleton<ITool, MemoryReadTool>();
        builder.Services.AddSingleton<ITool, MemoryWriteTool>();
        builder.Services.AddSingleton<ITool, MemoryListTool>();

        builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();

        // Slice 0011: permission gate + checkpoint service + session helpers
        builder.Services.AddSingleton<IPermissionGate, PermissionGateService>();
        builder.Services.AddSingleton<ICheckpointService, LibGit2CheckpointService>();
        builder.Services.AddSingleton<ConversationHistoryLoader>();

        // Slice 0018 — remote / cloud / background-service registrations
        builder.Services.Configure<RemoteClientOptions>(
            builder.Configuration.GetSection(RemoteClientOptions.SectionName));
        builder.Services.PostConfigure<RemoteClientOptions>(opt =>
        {
            opt.Endpoint = FirstNonEmpty(
                Environment.GetEnvironmentVariable("NEXCODE_REMOTE_ENDPOINT"),
                opt.Endpoint);
        });
        builder.Services.AddSingleton<RemoteSessionClient>();
        builder.Services.AddSingleton<RemoteSessionRouter>();
        builder.Services.AddSingleton<CloudExecutionRouter>();
        builder.Services.AddSingleton<StartupRegistration>(_ => new StartupRegistration());
        builder.Services.AddSingleton<TrayIconHost>();

        // Slice 0017 — plugins / automations / history / telemetry / environments
        builder.Services.AddHttpClient(nameof(Automations.AutomationStepExecutor));
        builder.Services.AddHttpClient(nameof(Telemetry.TelemetrySender));
        builder.Services.AddSingleton<PluginSignatureVerifier>();
        builder.Services.AddSingleton<PluginManager>();
        builder.Services.AddSingleton<PluginSandboxRunner>();
        builder.Services.AddSingleton<PluginHookDispatcher>();
        builder.Services.AddSingleton<AutomationStepExecutor>();
        builder.Services.AddSingleton<AutomationEngine>();
        builder.Services.AddSingleton<HistorySearchService>();
        builder.Services.AddSingleton<AutoTitleGenerator>();
        builder.Services.AddSingleton<TelemetryQueue>();
        builder.Services.AddSingleton<TelemetryConsentService>();
        builder.Services.AddSingleton<EnvironmentManager>();

        // Slice 0015 — terminal / LSP managers + tools
        builder.Services.AddSingleton<TerminalManager>();
        builder.Services.AddSingleton<LspManager>();
        builder.Services.AddSingleton<ITool, OpenTerminalTool>();
        builder.Services.AddSingleton<ITool, LspHoverTool>();
        builder.Services.AddSingleton<ITool, LspDiagnosticsTool>();

        // Slice 0016 — MCP client, sub-agents, marketplace demo
        builder.Services.AddSingleton<McpManager>();
        builder.Services.AddSingleton<NexCode.Service.Mcp.Auth.McpOauthPkceClient>();
        builder.Services.AddSingleton<SubAgentManager>();
        builder.Services.AddSingleton<MarketplaceFixtureService>();
        builder.Services.AddSingleton<ITool, McpCallToolTool>();
        builder.Services.AddSingleton<ITool, SpawnSubAgentTool>();
        builder.Services.AddSingleton<ITool, KillSubAgentTool>();

        // Background services
        builder.Services.AddHostedService<AuthStateWarmupBackgroundService>();
        builder.Services.AddHostedService<SubscriptionRefreshBackgroundService>();
        builder.Services.AddHostedService<ModesBootstrap>();
        builder.Services.AddHostedService<PersonalitiesBootstrap>();
        builder.Services.AddHostedService<PipeServerBackgroundService>();
        builder.Services.AddHostedService<AutomationScheduler>();
        builder.Services.AddHostedService<TelemetrySender>();
        builder.Services.AddHostedService<McpAutoConnectBootstrap>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TrayIconHost>());

        // Encrypted data layer
        builder.Services.AddSingleton<IDatabaseKeyProvider>(_ => DpapiDatabaseKeyProvider.FromLocalAppData());
        builder.Services.AddNexCodeData(GetDataSourcePath());

        var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var databaseInitializer = scope.ServiceProvider.GetRequiredService<NexCodeDatabaseInitializer>();
        await databaseInitializer.EnsureCreatedAsync();
        await host.RunAsync();
    }

    private static string GetDataSourcePath()
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexCode",
            "Data");

        Directory.CreateDirectory(dataRoot);
        return Path.Combine(dataRoot, "nexcode.db");
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        return !string.IsNullOrWhiteSpace(preferred)
            ? preferred.Trim()
            : fallback;
    }
}
