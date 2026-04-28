using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using NexCode.Data.Storage;
using NexCode.Data.Extensions;
using NexCode.Service.Auth;
using NexCode.Service.Git;
using NexCode.Service.Permissions;
using NexCode.Service.Providers;
using NexCode.Service.Sessions;
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
        builder.Services.AddSingleton<IModelProvider, AnthropicMessagesProvider>();
        builder.Services.AddSingleton<IModelProvider, OpenAIResponsesProvider>();
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
        builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();

        // Slice 0011: permission gate + checkpoint service + session helpers
        builder.Services.AddSingleton<IPermissionGate, PermissionGateService>();
        builder.Services.AddSingleton<ICheckpointService, LibGit2CheckpointService>();
        builder.Services.AddSingleton<ConversationHistoryLoader>();

        // Background services
        builder.Services.AddHostedService<AuthStateWarmupBackgroundService>();
        builder.Services.AddHostedService<SubscriptionRefreshBackgroundService>();
        builder.Services.AddHostedService<PipeServerBackgroundService>();

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
