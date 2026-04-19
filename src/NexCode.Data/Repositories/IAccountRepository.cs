using NexCode.Data.Entities;
using NexCode.Shared.Models;

namespace NexCode.Data.Repositories;

public interface IAccountRepository
{
    Task<UserEntity?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<UserEntity?> GetMostRecentUserAsync(CancellationToken cancellationToken = default);

    Task<UserEntity> UpsertUserAsync(
        string email,
        string? tokenCacheReference,
        SubscriptionTier subscriptionTier,
        bool isSuperUser,
        DateTimeOffset? lastAuthenticatedAt,
        CancellationToken cancellationToken = default);

    Task<IapCacheEntity?> GetLatestIapCacheAsync(CancellationToken cancellationToken = default);

    Task<IapCacheEntity> SaveIapCacheAsync(
        IReadOnlyCollection<string> productIds,
        DateTimeOffset verifiedAt,
        DateTimeOffset? expiresAt,
        string source,
        string? lastError,
        string? receiptJson = null,
        CancellationToken cancellationToken = default);
}
