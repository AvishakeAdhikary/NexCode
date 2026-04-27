using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Service.Auth;

public static class SubscriptionCapabilityPolicy
{
    public static SubscriptionCapabilitiesPayload BuildCapabilities(SubscriptionTier tier)
    {
        return tier switch
        {
            SubscriptionTier.SuperUser => new SubscriptionCapabilitiesPayload(
                CanUseSandbox: true,
                CanUseRemoteExecution: true,
                CanUseCloudExecution: true,
                MaxConcurrentSessions: int.MaxValue,
                MaxSubAgentsPerSession: int.MaxValue),
            SubscriptionTier.Enterprise => new SubscriptionCapabilitiesPayload(
                CanUseSandbox: true,
                CanUseRemoteExecution: true,
                CanUseCloudExecution: true,
                MaxConcurrentSessions: int.MaxValue,
                MaxSubAgentsPerSession: int.MaxValue),
            SubscriptionTier.Team => new SubscriptionCapabilitiesPayload(
                CanUseSandbox: true,
                CanUseRemoteExecution: true,
                CanUseCloudExecution: true,
                MaxConcurrentSessions: 20,
                MaxSubAgentsPerSession: 10),
            SubscriptionTier.Pro => new SubscriptionCapabilitiesPayload(
                CanUseSandbox: true,
                CanUseRemoteExecution: true,
                CanUseCloudExecution: false,
                MaxConcurrentSessions: 5,
                MaxSubAgentsPerSession: 3),
            _ => new SubscriptionCapabilitiesPayload(
                CanUseSandbox: false,
                CanUseRemoteExecution: false,
                CanUseCloudExecution: false,
                MaxConcurrentSessions: 1,
                MaxSubAgentsPerSession: 0)
        };
    }

    public static (bool Allowed, SubscriptionTier RequiredTier, string Message) ValidateSessionRequest(
        SessionCreateRequest request,
        SubscriptionCapabilitiesPayload capabilities,
        int activeSessions)
    {
        if (request.SandboxEnabled && !capabilities.CanUseSandbox)
        {
            return (false, SubscriptionTier.Pro, "Sandbox mode requires NexCode Pro or higher.");
        }

        if (request.ExecutionMode == ExecutionMode.Remote && !capabilities.CanUseRemoteExecution)
        {
            return (false, SubscriptionTier.Pro, "Remote execution requires NexCode Pro or higher.");
        }

        if (request.ExecutionMode == ExecutionMode.Cloud && !capabilities.CanUseCloudExecution)
        {
            return (false, SubscriptionTier.Team, "Cloud execution requires NexCode Team or higher.");
        }

        if (activeSessions >= capabilities.MaxConcurrentSessions)
        {
            return capabilities.MaxConcurrentSessions switch
            {
                1 => (false, SubscriptionTier.Pro, "Free tier allows only 1 concurrent session. Upgrade to Pro or higher to open more sessions."),
                5 => (false, SubscriptionTier.Team, "Pro tier allows up to 5 concurrent sessions. Upgrade to Team or higher for more."),
                20 => (false, SubscriptionTier.Enterprise, "Team tier allows up to 20 concurrent sessions. Upgrade to Enterprise for unlimited sessions."),
                _ => (false, SubscriptionTier.Enterprise, "Your current tier has reached its concurrent session limit.")
            };
        }

        return (true, SubscriptionTier.Free, string.Empty);
    }
}
