using System.Text.Json;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Clarify.Tests;

public sealed class ServiceHealthSerializationTests
{
    [Fact]
    public void JsonSerialization_UsesCamelCaseForSharedEnums()
    {
        var payload = new ServiceHealthPayload(
            State: ServiceHealthState.Healthy,
            Version: "0.1.0",
            ActiveSessions: 1,
            Timestamp: DateTimeOffset.Parse("2026-04-18T00:00:00Z"),
            PipeName: "nexcode-service-dev");

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);

        Assert.Contains("\"state\":\"healthy\"", json);
        Assert.Contains("\"pipeName\":\"nexcode-service-dev\"", json);
    }

    [Fact]
    public void AccountSnapshotSerialization_IncludesTierAndAuthFlags()
    {
        var payload = new AccountSnapshotPayload(
            new AuthStatePayload(
                IsAuthenticated: true,
                UserEmail: "privileged@example.test",
                IsSuperUser: true,
                HasMsalConfiguration: true,
                HasCachedToken: true,
                RequiresAuthentication: false,
                LastAuthenticatedAt: DateTimeOffset.Parse("2026-04-18T00:00:00Z")),
            new SubscriptionStatePayload(
                Tier: SubscriptionTier.SuperUser,
                VerifiedAt: DateTimeOffset.Parse("2026-04-18T00:00:00Z"),
                ExpiresAt: null,
                ProductIds: ["sealed-superuser-grant"],
                IsExpired: false,
                Source: "sealed-superuser-grant",
                Warning: null));

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);

        Assert.Contains("\"isSuperUser\":true", json);
        Assert.Contains("\"tier\":\"superUser\"", json);
        Assert.Contains("\"hasCachedToken\":true", json);
    }

    [Fact]
    public void ServiceEventSerialization_IncludesEventTypeAndSequence()
    {
        var payload = new ServiceEventsPollResponse(
            LatestSequence: 2,
            Events:
            [
                new ServiceEventEnvelope(
                    Sequence: 2,
                    EventType: ServiceEventTypes.AuthRequired,
                    Timestamp: DateTimeOffset.Parse("2026-04-19T00:00:00Z"),
                    Payload: JsonSerializer.SerializeToElement(
                        new AuthRequiredEventPayload(
                            Reason: "interactive_sign_in_required",
                            HasMsalConfiguration: true,
                            HasCachedToken: false),
                        JsonSerialization.Options))
            ]);

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);

        Assert.Contains("\"latestSequence\":2", json);
        Assert.Contains("\"eventType\":\"auth.required\"", json);
    }

    [Fact]
    public void SessionLifecycleEventSerialization_UsesSharedEnvelopeModel()
    {
        var payload = new ServiceEventsPollResponse(
            LatestSequence: 3,
            Events:
            [
                new ServiceEventEnvelope(
                    Sequence: 3,
                    EventType: ServiceEventTypes.SessionLifecycle,
                    Timestamp: DateTimeOffset.Parse("2026-04-19T00:10:00Z"),
                    Payload: JsonSerializer.SerializeToElement(
                        new SessionLifecycleEventPayload(
                            SessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            State: "created",
                            ProjectPath: "C:\\Projects\\NexCode",
                            Mode: "Code",
                            ExecutionMode: "Local",
                            Reason: null),
                        JsonSerialization.Options))
            ]);

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);

        Assert.Contains("\"eventType\":\"session.lifecycle\"", json);
        Assert.Contains("\"state\":\"created\"", json);
        Assert.Contains("\"executionMode\":\"Local\"", json);
    }

    [Fact]
    public void SubscriptionRefreshResponseSerialization_IncludesStoreRefreshFlags()
    {
        var payload = new AccountRefreshSubscriptionResponse(
            Snapshot: new AccountSnapshotPayload(
                new AuthStatePayload(
                    IsAuthenticated: true,
                    UserEmail: "store-user@example.test",
                    IsSuperUser: false,
                    HasMsalConfiguration: true,
                    HasCachedToken: true,
                    RequiresAuthentication: false,
                    LastAuthenticatedAt: DateTimeOffset.Parse("2026-04-19T00:00:00Z")),
                new SubscriptionStatePayload(
                    Tier: SubscriptionTier.Pro,
                    VerifiedAt: DateTimeOffset.Parse("2026-04-19T00:05:00Z"),
                    ExpiresAt: DateTimeOffset.Parse("2026-04-20T00:05:00Z"),
                    ProductIds: ["nexcode_pro_monthly"],
                    IsExpired: false,
                    Source: "windows-store",
                    Warning: null)),
            StoreAttempted: true,
            StoreRefreshSucceeded: true,
            UsedCachedFallback: false,
            StatusMessage: "Microsoft Store validation completed.");

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);

        Assert.Contains("\"storeAttempted\":true", json);
        Assert.Contains("\"storeRefreshSucceeded\":true", json);
        Assert.Contains("\"usedCachedFallback\":false", json);
        Assert.Contains("\"source\":\"windows-store\"", json);
    }
}
