using NexCode.Service.Auth;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests;

public sealed class SubscriptionCapabilityPolicyTests
{
    [Fact]
    public void BuildCapabilities_FreeTierMatchesSpecMatrix()
    {
        var capabilities = SubscriptionCapabilityPolicy.BuildCapabilities(SubscriptionTier.Free);

        Assert.False(capabilities.CanUseSandbox);
        Assert.False(capabilities.CanUseRemoteExecution);
        Assert.False(capabilities.CanUseCloudExecution);
        Assert.Equal(1, capabilities.MaxConcurrentSessions);
        Assert.Equal(0, capabilities.MaxSubAgentsPerSession);
    }

    [Fact]
    public void ValidateSessionRequest_FreeTierRejectsSandbox()
    {
        var request = new SessionCreateRequest(
            ProjectPath: "C:\\Projects\\NexCode",
            Mode: SessionMode.Code,
            ExecutionMode: ExecutionMode.Local,
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: true);

        var result = SubscriptionCapabilityPolicy.ValidateSessionRequest(
            request,
            SubscriptionCapabilityPolicy.BuildCapabilities(SubscriptionTier.Free),
            activeSessions: 0);

        Assert.False(result.Allowed);
        Assert.Equal(SubscriptionTier.Pro, result.RequiredTier);
        Assert.Contains("Sandbox mode", result.Message);
    }

    [Fact]
    public void ValidateSessionRequest_ProTierRejectsCloudExecution()
    {
        var request = new SessionCreateRequest(
            ProjectPath: "C:\\Projects\\NexCode",
            Mode: SessionMode.Code,
            ExecutionMode: ExecutionMode.Cloud,
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: false);

        var result = SubscriptionCapabilityPolicy.ValidateSessionRequest(
            request,
            SubscriptionCapabilityPolicy.BuildCapabilities(SubscriptionTier.Pro),
            activeSessions: 0);

        Assert.False(result.Allowed);
        Assert.Equal(SubscriptionTier.Team, result.RequiredTier);
        Assert.Contains("Cloud execution", result.Message);
    }

    [Fact]
    public void ValidateSessionRequest_FreeTierRejectsSecondConcurrentSession()
    {
        var request = new SessionCreateRequest(
            ProjectPath: "C:\\Projects\\NexCode",
            Mode: SessionMode.Code,
            ExecutionMode: ExecutionMode.Local,
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: false);

        var result = SubscriptionCapabilityPolicy.ValidateSessionRequest(
            request,
            SubscriptionCapabilityPolicy.BuildCapabilities(SubscriptionTier.Free),
            activeSessions: 1);

        Assert.False(result.Allowed);
        Assert.Equal(SubscriptionTier.Pro, result.RequiredTier);
        Assert.Contains("1 concurrent session", result.Message);
    }
}
