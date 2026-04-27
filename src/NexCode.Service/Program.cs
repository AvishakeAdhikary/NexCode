using System.Runtime.Versioning;
using NexCode.Data.Storage;
using NexCode.Data.Extensions;
using NexCode.Service.Auth;
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
        builder.Services.AddSingleton<SessionRegistry>();
        builder.Services.AddSingleton<ServiceEventHub>();
        builder.Services.AddSingleton<AccountStateService>();
        builder.Services.AddSingleton<SessionTurnService>();
        builder.Services.AddSingleton<MsalTokenCacheStore>();
        builder.Services.AddSingleton<IMsalAuthService, MsalAuthService>();
        builder.Services.AddSingleton<ISuperUserGrantService, SuperUserGrantService>();
        builder.Services.AddSingleton<IStoreSubscriptionService, WindowsStoreSubscriptionService>();
        builder.Services.AddHostedService<AuthStateWarmupBackgroundService>();
        builder.Services.AddHostedService<SubscriptionRefreshBackgroundService>();
        builder.Services.AddHostedService<PipeServerBackgroundService>();
        builder.Services.AddNexCodeData(GetConnectionString());

        var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var databaseInitializer = scope.ServiceProvider.GetRequiredService<NexCodeDatabaseInitializer>();
        await databaseInitializer.EnsureCreatedAsync();
        await host.RunAsync();
    }

    private static string GetConnectionString()
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexCode",
            "Data");

        Directory.CreateDirectory(dataRoot);
        return $"Data Source={Path.Combine(dataRoot, "nexcode.db")}";
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        return !string.IsNullOrWhiteSpace(preferred)
            ? preferred.Trim()
            : fallback;
    }
}
