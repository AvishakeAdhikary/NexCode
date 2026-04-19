using Grpc.Core;
using Microsoft.Extensions.Options;

namespace NexCode.Remote.Services;

public sealed class RemoteControlService(
    ILogger<RemoteControlService> logger,
    IOptions<RemoteHostOptions> options) : RemoteControl.RemoteControlBase
{
    public override Task<RemoteHealthReply> GetRemoteHealth(RemoteHealthRequest request, ServerCallContext context)
    {
        logger.LogInformation("Remote health requested from {Peer}", context.Peer);

        return Task.FromResult(new RemoteHealthReply
        {
            ServiceName = "nexcode-remote",
            State = "healthy",
            Version = options.Value.Version,
            Transport = options.Value.Transport,
            Message = "Remote execution foundation is available. CLI session forwarding will be layered in future slices."
        });
    }
}
