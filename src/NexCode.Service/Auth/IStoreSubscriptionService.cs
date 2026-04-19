namespace NexCode.Service.Auth;

public interface IStoreSubscriptionService
{
    Task<StoreSubscriptionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed record StoreSubscriptionRefreshResult(
    bool Succeeded,
    string[] ProductIds,
    string Source,
    string? Warning,
    string? ErrorMessage,
    string ReceiptJson);
