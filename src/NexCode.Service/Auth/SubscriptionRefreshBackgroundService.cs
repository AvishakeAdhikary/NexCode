namespace NexCode.Service.Auth;

public sealed class SubscriptionRefreshBackgroundService(
    ILogger<SubscriptionRefreshBackgroundService> logger,
    AccountStateService accountStateService,
    Microsoft.Extensions.Options.IOptions<IapOptions> iapOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshInterval = TimeSpan.FromHours(Math.Max(1, iapOptions.Value.CacheLifetimeHours));
        using var timer = new PeriodicTimer(refreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await accountStateService.RefreshSubscriptionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background Microsoft Store subscription refresh failed.");
            }
        }
    }
}
