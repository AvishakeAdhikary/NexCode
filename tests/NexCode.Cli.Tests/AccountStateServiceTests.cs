using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexCode.Data.Repositories;
using NexCode.Data.Storage;
using NexCode.Service;
using NexCode.Service.Auth;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests;

public sealed class AccountStateServiceTests
{
    [Fact]
    public async Task ValidSuperUserGrant_OverridesSubscriptionTier()
    {
        var repository = CreateRepository(out var databasePath);

        try
        {
            var iapOptions = Options.Create(new IapOptions());

            var service = new AccountStateService(
                iapOptions,
                repository,
                new FakeMsalAuthService(new MsalAuthSnapshot(
                    IsConfigured: true,
                    IsAuthenticated: true,
                    HasCachedToken: false,
                    RequiresAuthentication: false,
                    UserEmail: "privileged@example.test",
                    TokenCacheReference: "C:\\cache\\msal.bin",
                    LastAuthenticatedAt: DateTimeOffset.Parse("2026-04-18T12:00:00Z"))),
                new FakeSuperUserGrantService(true),
                new FakeStoreSubscriptionService(new StoreSubscriptionRefreshResult(
                    Succeeded: true,
                    ProductIds: ["nexcode_pro_monthly"],
                    Source: "windows-store",
                    Warning: null,
                    ErrorMessage: null,
                    ReceiptJson: "{}")),
                new ServiceEventHub());

            var snapshot = await service.GetSnapshotAsync();
            var user = await repository.GetUserByEmailAsync("privileged@example.test");

            Assert.True(snapshot.Auth.IsAuthenticated);
            Assert.True(snapshot.Auth.IsSuperUser);
            Assert.Equal(SubscriptionTier.SuperUser, snapshot.Subscription.Tier);
            Assert.NotNull(user);
            Assert.Equal(SubscriptionTier.SuperUser, user!.SubscriptionTier);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ProductIdMapping_ResolvesHighestTier_AndPersistsCache()
    {
        var repository = CreateRepository(out var databasePath);

        try
        {
            var iapOptions = Options.Create(new IapOptions());

            var service = new AccountStateService(
                iapOptions,
                repository,
                new FakeMsalAuthService(new MsalAuthSnapshot(
                    IsConfigured: false,
                    IsAuthenticated: false,
                    HasCachedToken: false,
                    RequiresAuthentication: false,
                    UserEmail: null,
                    TokenCacheReference: null,
                    LastAuthenticatedAt: null)),
                new FakeSuperUserGrantService(false),
                new FakeStoreSubscriptionService(new StoreSubscriptionRefreshResult(
                    Succeeded: true,
                    ProductIds: ["nexcode_team_monthly", "nexcode_pro_annual"],
                    Source: "windows-store",
                    Warning: null,
                    ErrorMessage: null,
                    ReceiptJson: "{\"productAddOns\":[]}")),
                new ServiceEventHub());

            var snapshot = await service.GetSnapshotAsync();
            var cache = await repository.GetLatestIapCacheAsync();

            Assert.Equal(SubscriptionTier.Team, snapshot.Subscription.Tier);
            Assert.False(snapshot.Auth.IsAuthenticated);
            Assert.NotNull(cache);
            Assert.Equal("windows-store", cache!.Source);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task CachedTokenWithoutSilentRefresh_RequiresAuthentication()
    {
        var repository = CreateRepository(out var databasePath);

        try
        {
            var iapOptions = Options.Create(new IapOptions());

            var service = new AccountStateService(
                iapOptions,
                repository,
                new FakeMsalAuthService(new MsalAuthSnapshot(
                    IsConfigured: true,
                    IsAuthenticated: false,
                    HasCachedToken: true,
                    RequiresAuthentication: true,
                    UserEmail: "cached@example.com",
                    TokenCacheReference: "C:\\cache\\msal.bin",
                    LastAuthenticatedAt: DateTimeOffset.Parse("2026-04-17T12:00:00Z"))),
                new FakeSuperUserGrantService(false),
                new FakeStoreSubscriptionService(new StoreSubscriptionRefreshResult(
                    Succeeded: true,
                    ProductIds: [],
                    Source: "windows-store",
                    Warning: null,
                    ErrorMessage: null,
                    ReceiptJson: "{}")),
                new ServiceEventHub());

            var snapshot = await service.GetSnapshotAsync();

            Assert.False(snapshot.Auth.IsAuthenticated);
            Assert.True(snapshot.Auth.HasCachedToken);
            Assert.True(snapshot.Auth.RequiresAuthentication);
            Assert.Equal("cached@example.com", snapshot.Auth.UserEmail);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RefreshFailure_UsesCachedTier_WhenCacheIsStillValid()
    {
        var repository = CreateRepository(out var databasePath);

        try
        {
            await repository.SaveIapCacheAsync(
                productIds: ["nexcode_team_monthly"],
                verifiedAt: DateTimeOffset.UtcNow.AddHours(-2),
                expiresAt: DateTimeOffset.UtcNow.AddHours(22),
                source: "windows-store",
                lastError: null,
                receiptJson: "{}");

            var service = new AccountStateService(
                Options.Create(new IapOptions()),
                repository,
                new FakeMsalAuthService(new MsalAuthSnapshot(
                    IsConfigured: true,
                    IsAuthenticated: true,
                    HasCachedToken: true,
                    RequiresAuthentication: false,
                    UserEmail: "cached-tier@example.test",
                    TokenCacheReference: "C:\\cache\\msal.bin",
                    LastAuthenticatedAt: DateTimeOffset.UtcNow.AddMinutes(-30))),
                new FakeSuperUserGrantService(false),
                new FakeStoreSubscriptionService(new StoreSubscriptionRefreshResult(
                    Succeeded: false,
                    ProductIds: [],
                    Source: "windows-store",
                    Warning: "Microsoft Store validation is currently unavailable.",
                    ErrorMessage: "store offline",
                    ReceiptJson: "{}")),
                new ServiceEventHub());

            var response = await service.RefreshSubscriptionAsync();

            Assert.Equal(SubscriptionTier.Team, response.Snapshot.Subscription.Tier);
            Assert.True(response.UsedCachedFallback);
            Assert.False(response.StoreRefreshSucceeded);
            Assert.Contains("cached subscription state", response.StatusMessage);
            Assert.Contains("store offline", response.Snapshot.Subscription.Warning);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RefreshFailure_DegradesToFree_WhenCacheIsExpired()
    {
        var repository = CreateRepository(out var databasePath);

        try
        {
            await repository.SaveIapCacheAsync(
                productIds: ["nexcode_enterprise_annual"],
                verifiedAt: DateTimeOffset.UtcNow.AddDays(-2),
                expiresAt: DateTimeOffset.UtcNow.AddHours(-1),
                source: "windows-store",
                lastError: null,
                receiptJson: "{}");

            var service = new AccountStateService(
                Options.Create(new IapOptions()),
                repository,
                new FakeMsalAuthService(new MsalAuthSnapshot(
                    IsConfigured: true,
                    IsAuthenticated: true,
                    HasCachedToken: true,
                    RequiresAuthentication: false,
                    UserEmail: "expired-cache@example.test",
                    TokenCacheReference: "C:\\cache\\msal.bin",
                    LastAuthenticatedAt: DateTimeOffset.UtcNow.AddMinutes(-30))),
                new FakeSuperUserGrantService(false),
                new FakeStoreSubscriptionService(new StoreSubscriptionRefreshResult(
                    Succeeded: false,
                    ProductIds: [],
                    Source: "windows-store",
                    Warning: "Microsoft Store validation is currently unavailable.",
                    ErrorMessage: "store unavailable",
                    ReceiptJson: "{}")),
                new ServiceEventHub());

            var response = await service.RefreshSubscriptionAsync();

            Assert.Equal(SubscriptionTier.Free, response.Snapshot.Subscription.Tier);
            Assert.True(response.Snapshot.Subscription.IsExpired);
            Assert.False(response.UsedCachedFallback);
            Assert.Contains("Free tier", response.StatusMessage);
            Assert.Contains("store unavailable", response.Snapshot.Subscription.Warning);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static IAccountRepository CreateRepository(out string databasePath)
    {
        SqlCipherBootstrapper.EnsureInitialized();

        databasePath = Path.Combine(Path.GetTempPath(), $"nexcode-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        using (var dbContext = new NexCodeDbContext(options))
        {
            dbContext.Database.EnsureCreated();
        }

        return new AccountRepository(new TestDbContextFactory(options));
    }

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     $"{databasePath}-shm",
                     $"{databasePath}-wal"
                 })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // SQLite file handles can linger briefly after test completion; cleanup is best-effort only.
            }
        }
    }

    private sealed class FakeMsalAuthService(MsalAuthSnapshot snapshot) : IMsalAuthService
    {
        public Task<MsalAuthSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }

        public Task<MsalAuthSnapshot> SignInInteractiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeSuperUserGrantService(bool isValid) : ISuperUserGrantService
    {
        public Task<bool> HasValidGrantAsync(string? email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(isValid);
        }
    }

    private sealed class FakeStoreSubscriptionService(StoreSubscriptionRefreshResult result) : IStoreSubscriptionService
    {
        public Task<StoreSubscriptionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<NexCodeDbContext> options) : IDbContextFactory<NexCodeDbContext>
    {
        public NexCodeDbContext CreateDbContext()
        {
            return new NexCodeDbContext(options);
        }

        public Task<NexCodeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
