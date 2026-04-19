namespace NexCode.Remote;

using NexCode.Remote.Services;

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
        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapGrpcService<RemoteControlService>();
        app.MapGet(
            "/",
            () => "NexCode remote foundation is running. Use a gRPC client to query RemoteControl/GetRemoteHealth.");

        app.Run();
    }
}
