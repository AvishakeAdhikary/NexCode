namespace NexCode.Shared.Contracts;

public sealed record AccountSignInRequest(
    bool ForceInteractive = true);

public sealed record AccountRefreshSubscriptionRequest(
    bool ForceRefresh = true);

public sealed record AccountRefreshSubscriptionResponse(
    AccountSnapshotPayload Snapshot,
    bool StoreAttempted,
    bool StoreRefreshSucceeded,
    bool UsedCachedFallback,
    string? StatusMessage);
