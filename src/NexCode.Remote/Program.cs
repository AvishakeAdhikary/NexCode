using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexCode.Remote.Security;
using NexCode.Remote.Services;

namespace NexCode.Remote;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Services.Configure<RemoteHostOptions>(
            builder.Configuration.GetSection(RemoteHostOptions.SectionName));

        // Spec §23.3 — Kestrel mTLS endpoint + JWT bearer.
        MtlsConfiguration.ConfigureKestrel(builder);
        builder.Services.AddMsalJwtBearer(builder.Configuration);

        // Adapter used by RemoteControlService.OpenSession to dispatch decrypted
        // session traffic. Echo by default; production deployments replace this.
        builder.Services.AddSingleton<IRemoteSessionAdapter, EchoRemoteSessionAdapter>();
        builder.Services.AddSingleton<IRemoteSigningKeyProvider, EnvRemoteSigningKeyProvider>();

        builder.Services.AddGrpc();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGrpcService<RemoteControlService>();
        app.MapGet(
            "/",
            () => "NexCode remote is running. Use a gRPC client (mTLS + Bearer JWT) to call RemoteControl.");

        app.Run();
    }
}
