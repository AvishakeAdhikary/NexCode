using System.Text.Json;
using Microsoft.Extensions.Options;
using NexCode.Data.Entities;
using NexCode.Data.Repositories;
using NexCode.Service;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Auth;

public sealed class AccountStateService(
    IOptions<IapOptions> iapOptions,
    IAccountRepository accountRepository,
    IMsalAuthService msalAuthService,
    ISuperUserGrantService superUserGrantService,
    IStoreSubscriptionService storeSubscriptionService,
    ServiceEventHub serviceEventHub)
{
    private readonly Lock _eventSync = new();
    private string? _lastAuthEventFingerprint;

    public async Task<AccountSnapshotPayload> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var authResolution = await ResolveAuthStateAsync(
            useInteractiveSignIn: false,
            cancellationToken);
        var subscription = await ResolveSubscriptionStateAsync(
            authResolution.Payload,
            forceStoreRefresh: false,
            cancellationToken);
        var capabilities = SubscriptionCapabilityPolicy.BuildCapabilities(subscription.Payload.Tier);
        await PersistUserAsync(authResolution, subscription.Payload.Tier, cancellationToken);
        PublishAuthEventIfChanged(authResolution.Payload);
        return new AccountSnapshotPayload(authResolution.Payload, subscription.Payload, capabilities);
    }

    public async Task<AccountSnapshotPayload> SignInAsync(CancellationToken cancellationToken = default)
    {
        var authResolution = await ResolveAuthStateAsync(
            useInteractiveSignIn: true,
            cancellationToken);
        var subscription = await ResolveSubscriptionStateAsync(
            authResolution.Payload,
            forceStoreRefresh: false,
            cancellationToken);
        var capabilities = SubscriptionCapabilityPolicy.BuildCapabilities(subscription.Payload.Tier);
        await PersistUserAsync(authResolution, subscription.Payload.Tier, cancellationToken);
        PublishAuthEventIfChanged(authResolution.Payload);
        return new AccountSnapshotPayload(authResolution.Payload, subscription.Payload, capabilities);
    }

    public async Task<AccountRefreshSubscriptionResponse> RefreshSubscriptionAsync(
        CancellationToken cancellationToken = default)
    {
        var authResolution = await ResolveAuthStateAsync(
            useInteractiveSignIn: false,
            cancellationToken);
        var subscription = await ResolveSubscriptionStateAsync(
            authResolution.Payload,
            forceStoreRefresh: true,
            cancellationToken);
        var capabilities = SubscriptionCapabilityPolicy.BuildCapabilities(subscription.Payload.Tier);

        await PersistUserAsync(authResolution, subscription.Payload.Tier, cancellationToken);
        PublishAuthEventIfChanged(authResolution.Payload);

        return new AccountRefreshSubscriptionResponse(
            Snapshot: new AccountSnapshotPayload(authResolution.Payload, subscription.Payload, capabilities),
            StoreAttempted: subscription.StoreAttempted,
            StoreRefreshSucceeded: subscription.StoreRefreshSucceeded,
            UsedCachedFallback: subscription.UsedCachedFallback,
            StatusMessage: subscription.StatusMessage);
    }

    private async Task<ResolvedAuthState> ResolveAuthStateAsync(
        bool useInteractiveSignIn,
        CancellationToken cancellationToken)
    {
        var msalSnapshot = useInteractiveSignIn
            ? await msalAuthService.SignInInteractiveAsync(cancellationToken)
            : await msalAuthService.GetCurrentSnapshotAsync(cancellationToken);

        var existingUser = await GetExistingUserAsync(msalSnapshot.UserEmail, cancellationToken);
        var resolvedEmail = msalSnapshot.UserEmail ?? existingUser?.Email;
        var isAuthenticated = msalSnapshot.IsAuthenticated;
        var isSuperUser = isAuthenticated &&
                          await superUserGrantService.HasValidGrantAsync(resolvedEmail, cancellationToken);

        var payload = new AuthStatePayload(
            IsAuthenticated: isAuthenticated,
            UserEmail: resolvedEmail,
            IsSuperUser: isSuperUser,
            HasMsalConfiguration: msalSnapshot.IsConfigured,
            HasCachedToken: msalSnapshot.HasCachedToken || !string.IsNullOrWhiteSpace(existingUser?.MsalTokenCacheReference),
            RequiresAuthentication: !msalSnapshot.IsAuthenticated,
            LastAuthenticatedAt: msalSnapshot.LastAuthenticatedAt ?? existingUser?.UpdatedAt);

        return new ResolvedAuthState(
            Payload: payload,
            TokenCacheReference: msalSnapshot.TokenCacheReference ?? existingUser?.MsalTokenCacheReference);
    }

    private async Task<ResolvedSubscriptionState> ResolveSubscriptionStateAsync(
        AuthStatePayload auth,
        bool forceStoreRefresh,
        CancellationToken cancellationToken)
    {
        if (auth.IsSuperUser)
        {
            return new ResolvedSubscriptionState(
                Payload: new SubscriptionStatePayload(
                    Tier: SubscriptionTier.SuperUser,
                    VerifiedAt: DateTimeOffset.UtcNow,
                    ExpiresAt: null,
                    ProductIds: ["sealed-superuser-grant"],
                    IsExpired: false,
                    Source: "sealed-superuser-grant",
                    Warning: null),
                StoreAttempted: false,
                StoreRefreshSucceeded: false,
                UsedCachedFallback: false,
                StatusMessage: "Sealed superuser grant overrides Microsoft Store subscription checks.");
        }

        var cachedEntry = await accountRepository.GetLatestIapCacheAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var cacheLifetime = TimeSpan.FromHours(Math.Max(1, iapOptions.Value.CacheLifetimeHours));
        var shouldRefresh = forceStoreRefresh ||
                            cachedEntry is null ||
                            !string.Equals(cachedEntry.Source, "windows-store", StringComparison.OrdinalIgnoreCase) ||
                            cachedEntry.ExpiresAt is null ||
                            cachedEntry.ExpiresAt <= now;

        if (shouldRefresh)
        {
            var refreshResult = await storeSubscriptionService.RefreshAsync(cancellationToken);
            if (refreshResult.Succeeded)
            {
                cachedEntry = await accountRepository.SaveIapCacheAsync(
                    refreshResult.ProductIds,
                    verifiedAt: now,
                    expiresAt: now.Add(cacheLifetime),
                    source: refreshResult.Source,
                    lastError: null,
                    receiptJson: refreshResult.ReceiptJson,
                    cancellationToken: cancellationToken);

                var payload = BuildSubscriptionStateFromCache(cachedEntry, warningOverride: refreshResult.Warning);
                var message = payload.ProductIds.Length == 0
                    ? "Microsoft Store validation completed. No active paid subscription add-ons were found."
                    : $"Microsoft Store validation completed. Active products: {string.Join(", ", payload.ProductIds)}.";

                return new ResolvedSubscriptionState(
                    Payload: payload,
                    StoreAttempted: true,
                    StoreRefreshSucceeded: true,
                    UsedCachedFallback: false,
                    StatusMessage: message);
            }

            if (cachedEntry is not null)
            {
                cachedEntry = await accountRepository.SaveIapCacheAsync(
                    DeserializeProductIds(cachedEntry.ProductIdsJson),
                    verifiedAt: cachedEntry.VerifiedAt,
                    expiresAt: cachedEntry.ExpiresAt,
                    source: cachedEntry.Source,
                    lastError: refreshResult.ErrorMessage,
                    receiptJson: cachedEntry.ReceiptJson,
                    cancellationToken: cancellationToken);

                var payload = BuildSubscriptionStateFromCache(cachedEntry);
                var isFallbackUsable = !payload.IsExpired;
                var statusMessage = isFallbackUsable
                    ? "Microsoft Store validation failed. The helper is using the most recent cached subscription state."
                    : "Microsoft Store validation failed and the cached subscription state is expired. NexCode is running in Free tier until validation succeeds again.";

                return new ResolvedSubscriptionState(
                    Payload: payload,
                    StoreAttempted: true,
                    StoreRefreshSucceeded: false,
                    UsedCachedFallback: isFallbackUsable,
                    StatusMessage: statusMessage);
            }

            return new ResolvedSubscriptionState(
                Payload: new SubscriptionStatePayload(
                    Tier: SubscriptionTier.Free,
                    VerifiedAt: null,
                    ExpiresAt: null,
                    ProductIds: [],
                    IsExpired: false,
                    Source: "windows-store",
                    Warning: "Microsoft Store validation is unavailable and no cached subscription state exists yet. NexCode is running in Free tier."),
                StoreAttempted: true,
                StoreRefreshSucceeded: false,
                UsedCachedFallback: false,
                StatusMessage: "Microsoft Store validation failed and no cached subscription state was available.");
        }

        if (cachedEntry is null)
        {
            return new ResolvedSubscriptionState(
                Payload: new SubscriptionStatePayload(
                    Tier: SubscriptionTier.Free,
                    VerifiedAt: null,
                    ExpiresAt: null,
                    ProductIds: [],
                    IsExpired: false,
                    Source: "none",
                    Warning: null),
                StoreAttempted: false,
                StoreRefreshSucceeded: false,
                UsedCachedFallback: false,
                StatusMessage: "No subscription cache is available yet.");
        }

        return new ResolvedSubscriptionState(
            Payload: BuildSubscriptionStateFromCache(cachedEntry),
            StoreAttempted: false,
            StoreRefreshSucceeded: false,
            UsedCachedFallback: false,
            StatusMessage: "Using the current cached Microsoft Store subscription state.");
    }

    private async Task PersistUserAsync(
        ResolvedAuthState authResolution,
        SubscriptionTier subscriptionTier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authResolution.Payload.UserEmail))
        {
            return;
        }

        await accountRepository.UpsertUserAsync(
            authResolution.Payload.UserEmail,
            authResolution.TokenCacheReference,
            subscriptionTier,
            authResolution.Payload.IsSuperUser,
            authResolution.Payload.LastAuthenticatedAt,
            cancellationToken);
    }

    private async Task<UserEntity?> GetExistingUserAsync(
        string? email,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return await accountRepository.GetUserByEmailAsync(email, cancellationToken);
        }

        return await accountRepository.GetMostRecentUserAsync(cancellationToken);
    }

    private void PublishAuthEventIfChanged(AuthStatePayload authState)
    {
        var fingerprint = string.Join(
            "|",
            authState.IsAuthenticated,
            authState.IsSuperUser,
            authState.HasMsalConfiguration,
            authState.HasCachedToken,
            authState.RequiresAuthentication,
            authState.UserEmail ?? string.Empty);

        lock (_eventSync)
        {
            if (string.Equals(_lastAuthEventFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            _lastAuthEventFingerprint = fingerprint;
        }

        if (authState.IsAuthenticated)
        {
            serviceEventHub.Publish(
                ServiceEventTypes.AuthSuccess,
                new AuthSuccessEventPayload(authState.UserEmail, authState.IsSuperUser));
            return;
        }

        var reason = authState.HasMsalConfiguration
            ? authState.HasCachedToken
                ? "silent_refresh_failed"
                : "interactive_sign_in_required"
            : "msal_configuration_missing";

        serviceEventHub.Publish(
            ServiceEventTypes.AuthRequired,
            new AuthRequiredEventPayload(
                Reason: reason,
                HasMsalConfiguration: authState.HasMsalConfiguration,
                HasCachedToken: authState.HasCachedToken));
    }

    private static SubscriptionTier ResolveTier(IEnumerable<string> productIds)
    {
        var ids = productIds.ToArray();

        if (ids.Any(id => id.StartsWith("nexcode_enterprise_", StringComparison.OrdinalIgnoreCase)))
        {
            return SubscriptionTier.Enterprise;
        }

        if (ids.Any(id => id.StartsWith("nexcode_team_", StringComparison.OrdinalIgnoreCase)))
        {
            return SubscriptionTier.Team;
        }

        if (ids.Any(id => id.StartsWith("nexcode_pro_", StringComparison.OrdinalIgnoreCase)))
        {
            return SubscriptionTier.Pro;
        }

        return SubscriptionTier.Free;
    }

    private static string[] DeserializeProductIds(string productIdsJson)
    {
        return JsonSerializer.Deserialize<string[]>(productIdsJson, JsonSerialization.Options) ?? [];
    }

    private static SubscriptionStatePayload BuildSubscriptionStateFromCache(
        IapCacheEntity cachedEntry,
        string? warningOverride = null)
    {
        var productIds = DeserializeProductIds(cachedEntry.ProductIdsJson);
        var isExpired = cachedEntry.ExpiresAt is not null && cachedEntry.ExpiresAt <= DateTimeOffset.UtcNow;
        var tier = isExpired ? SubscriptionTier.Free : ResolveTier(productIds);
        var warning = warningOverride;

        if (string.IsNullOrWhiteSpace(warning))
        {
            warning = isExpired
                ? cachedEntry.LastError ?? "Subscription verification is stale. The app is temporarily running in Free tier until Microsoft Store validation is refreshed."
                : cachedEntry.LastError;
        }

        return new SubscriptionStatePayload(
            Tier: tier,
            VerifiedAt: cachedEntry.VerifiedAt,
            ExpiresAt: cachedEntry.ExpiresAt,
            ProductIds: productIds,
            IsExpired: isExpired,
            Source: cachedEntry.Source,
            Warning: warning);
    }

    private sealed record ResolvedAuthState(
        AuthStatePayload Payload,
        string? TokenCacheReference);

    private sealed record ResolvedSubscriptionState(
        SubscriptionStatePayload Payload,
        bool StoreAttempted,
        bool StoreRefreshSucceeded,
        bool UsedCachedFallback,
        string? StatusMessage);
}
