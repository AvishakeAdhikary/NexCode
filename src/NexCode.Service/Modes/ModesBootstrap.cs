using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Modes;

/// <summary>
/// Hosted service that idempotently seeds the four spec §7.1 built-in modes on helper startup.
/// Runs once per process; failures are logged but never crash the host because the mode catalog
/// is non-essential for raw IPC liveness.
/// </summary>
public sealed class ModesBootstrap(
    ModesService modesService,
    ILogger<ModesBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await modesService.EnsureBuiltInsAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Built-in modes seeded.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed built-in modes.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
