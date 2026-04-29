using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Personalities;

/// <summary>
/// Hosted service that idempotently seeds the spec §8.2 built-in personalities on helper
/// startup. Failures are logged but never abort the host because personalities are
/// non-essential for raw IPC liveness.
/// </summary>
public sealed class PersonalitiesBootstrap(
    PersonalitiesService personalitiesService,
    ILogger<PersonalitiesBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await personalitiesService.EnsureBuiltInsAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Built-in personalities seeded.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed built-in personalities.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
