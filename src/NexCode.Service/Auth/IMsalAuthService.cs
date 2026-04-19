namespace NexCode.Service.Auth;

public interface IMsalAuthService
{
    Task<MsalAuthSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default);

    Task<MsalAuthSnapshot> SignInInteractiveAsync(CancellationToken cancellationToken = default);
}
