using NexCode.Gui.Auth;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Clarify.Tests;

public sealed class AuthGatePresentationStateTests
{
    [Fact]
    public void AuthRequiredEvent_ShowsPollingAwareGate()
    {
        var state = new AuthGatePresentationState();

        state.RecordPollSuccess(DateTimeOffset.Parse("2026-04-19T09:00:00Z"));
        state.ApplyAuthRequiredEvent(
            new AuthRequiredEventPayload(
                Reason: "silent_refresh_failed",
                HasMsalConfiguration: true,
                HasCachedToken: true),
            DateTimeOffset.Parse("2026-04-19T09:00:05Z"));

        var presentation = state.Build();

        Assert.True(presentation.IsOverlayVisible);
        Assert.Equal("Microsoft sign-in required", presentation.Title);
        Assert.Contains("requested Microsoft sign-in", presentation.PrimaryStatus);
        Assert.Equal("Listening for helper updates.", presentation.EventStreamStatus);
        Assert.Contains("Silent token refresh failed", presentation.EventStreamDetail);
        Assert.True(presentation.IsSignInEnabled);
    }

    [Fact]
    public void RefreshFailure_AfterAuthenticatedSnapshot_DoesNotReblockDashboard()
    {
        var state = new AuthGatePresentationState();

        state.ApplySnapshot(
            new AccountSnapshotPayload(
                new AuthStatePayload(
                    IsAuthenticated: true,
                    UserEmail: "someone@example.test",
                    IsSuperUser: false,
                    HasMsalConfiguration: true,
                    HasCachedToken: true,
                    RequiresAuthentication: false,
                    LastAuthenticatedAt: DateTimeOffset.Parse("2026-04-19T08:55:00Z")),
                new SubscriptionStatePayload(
                    Tier: SubscriptionTier.Free,
                    VerifiedAt: DateTimeOffset.Parse("2026-04-19T08:55:00Z"),
                    ExpiresAt: null,
                    ProductIds: [],
                    IsExpired: false,
                    Source: "store",
                    Warning: null),
                new SubscriptionCapabilitiesPayload(
                    CanUseSandbox: false,
                    CanUseRemoteExecution: false,
                    CanUseCloudExecution: false,
                    MaxConcurrentSessions: 1,
                    MaxSubAgentsPerSession: 0)),
            DateTimeOffset.Parse("2026-04-19T08:55:30Z"));

        state.RecordRefreshFailure("The pipe is busy.");

        var presentation = state.Build();

        Assert.False(presentation.IsOverlayVisible);
        Assert.True(presentation.IsSignInEnabled);
        Assert.Equal("Refresh sign-in", presentation.SignInButtonLabel);
    }

    [Fact]
    public void PollReconnect_AfterStartupFailure_ShowsWaitingState()
    {
        var state = new AuthGatePresentationState();

        state.RecordRefreshFailure("No helper response.");
        state.RecordPollSuccess(DateTimeOffset.Parse("2026-04-19T09:10:00Z"));

        var presentation = state.Build();

        Assert.True(presentation.IsOverlayVisible);
        Assert.Equal("Checking account access", presentation.Title);
        Assert.Contains("reconnected", presentation.PrimaryStatus);
        Assert.Equal("Listening for helper updates.", presentation.EventStreamStatus);
        Assert.Equal("Sign in with Microsoft", presentation.SignInButtonLabel);
        Assert.False(presentation.IsSignInEnabled);
    }
}
