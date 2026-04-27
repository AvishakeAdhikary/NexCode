using NexCode.Shared.Models;

namespace NexCode.Shared.Contracts;

public sealed record ServiceHealthPayload(
    ServiceHealthState State,
    string Version,
    int ActiveSessions,
    DateTimeOffset Timestamp,
    string PipeName);

public sealed record AuthStatePayload(
    bool IsAuthenticated,
    string? UserEmail,
    bool IsSuperUser,
    bool HasMsalConfiguration,
    bool HasCachedToken,
    bool RequiresAuthentication,
    DateTimeOffset? LastAuthenticatedAt);

public sealed record SubscriptionStatePayload(
    SubscriptionTier Tier,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ExpiresAt,
    string[] ProductIds,
    bool IsExpired,
    string Source,
    string? Warning);

public sealed record SubscriptionCapabilitiesPayload(
    bool CanUseSandbox,
    bool CanUseRemoteExecution,
    bool CanUseCloudExecution,
    int MaxConcurrentSessions,
    int MaxSubAgentsPerSession);

public sealed record AccountSnapshotPayload(
    AuthStatePayload Auth,
    SubscriptionStatePayload Subscription,
    SubscriptionCapabilitiesPayload Capabilities);
