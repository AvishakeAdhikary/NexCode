using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Data.Repositories;

public sealed class AccountRepository(IDbContextFactory<NexCodeDbContext> dbContextFactory) : IAccountRepository
{
    public async Task<UserEntity?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Users
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    public async Task<UserEntity?> GetMostRecentUserAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await dbContext.Users.ToListAsync(cancellationToken))
            .OrderByDescending(user => user.UpdatedAt)
            .FirstOrDefault();
    }

    public async Task<UserEntity> UpsertUserAsync(
        string email,
        string? tokenCacheReference,
        SubscriptionTier subscriptionTier,
        bool isSuperUser,
        DateTimeOffset? lastAuthenticatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.Users.FirstOrDefaultAsync(
            user => user.Email == email,
            cancellationToken);

        var timestamp = lastAuthenticatedAt ?? DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = email,
                CreatedAt = timestamp
            };

            await dbContext.Users.AddAsync(entity, cancellationToken);
        }

        entity.MsalTokenCacheReference = tokenCacheReference;
        entity.SubscriptionTier = subscriptionTier;
        entity.IsSuperUser = isSuperUser;
        entity.UpdatedAt = timestamp;

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IapCacheEntity?> GetLatestIapCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await dbContext.IapCache.ToListAsync(cancellationToken))
            .OrderByDescending(entry => entry.UpdatedAt)
            .FirstOrDefault();
    }

    public async Task<IapCacheEntity> SaveIapCacheAsync(
        IReadOnlyCollection<string> productIds,
        DateTimeOffset verifiedAt,
        DateTimeOffset? expiresAt,
        string source,
        string? lastError,
        string? receiptJson = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = (await dbContext.IapCache.ToListAsync(cancellationToken))
            .OrderByDescending(entry => entry.UpdatedAt)
            .FirstOrDefault();

        if (entity is null)
        {
            entity = new IapCacheEntity
            {
                Id = Guid.NewGuid()
            };

            await dbContext.IapCache.AddAsync(entity, cancellationToken);
        }

        entity.ProductIdsJson = JsonSerializer.Serialize(productIds, JsonSerialization.Options);
        entity.VerifiedAt = verifiedAt;
        entity.ExpiresAt = expiresAt;
        entity.Source = source;
        entity.LastError = lastError;
        entity.ReceiptJson = string.IsNullOrWhiteSpace(receiptJson) ? "{}" : receiptJson;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }
}
