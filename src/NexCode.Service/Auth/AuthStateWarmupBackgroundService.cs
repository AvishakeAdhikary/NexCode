namespace NexCode.Service.Auth;

public sealed class AuthStateWarmupBackgroundService(
    ILogger<AuthStateWarmupBackgroundService> logger,
    AccountStateService accountStateService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await accountStateService.GetSnapshotAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auth state warmup failed during helper startup.");
        }
    }
}
