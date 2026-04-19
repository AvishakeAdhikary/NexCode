using NexCode.Service;
using NexCode.Shared.Contracts;

namespace NexCode.Cli.Tests;

public sealed class ServiceEventHubTests
{
    [Fact]
    public void PublishAndPoll_ReturnsEventsAfterSequenceInOrder()
    {
        var hub = new ServiceEventHub();

        var first = hub.Publish(
            ServiceEventTypes.AuthRequired,
            new AuthRequiredEventPayload(
                Reason: "interactive_sign_in_required",
                HasMsalConfiguration: true,
                HasCachedToken: false));
        var second = hub.Publish(
            ServiceEventTypes.SessionLifecycle,
            new SessionLifecycleEventPayload(
                SessionId: Guid.NewGuid(),
                State: "created",
                ProjectPath: "C:\\Projects\\NexCode",
                Mode: "Code",
                ExecutionMode: "Local",
                Reason: null));

        var response = hub.Poll(first.Sequence);

        Assert.Equal(second.Sequence, response.LatestSequence);
        Assert.Single(response.Events);
        Assert.Equal(ServiceEventTypes.SessionLifecycle, response.Events[0].EventType);
    }
}
