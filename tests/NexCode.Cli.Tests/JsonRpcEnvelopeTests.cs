using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests;

public sealed class JsonRpcEnvelopeTests
{
    [Fact]
    public void CreateRequest_RoundTripsTypedParameters()
    {
        var request = JsonRpcRequest.Create(
            "session.create",
            new SessionCreateRequest(
                ProjectPath: "C:\\Projects\\NexCode",
                Mode: SessionMode.Code,
                ExecutionMode: ExecutionMode.Local,
                PermissionLevel: PermissionLevel.Default,
                SandboxEnabled: false),
            id: "req-1");

        var payload = request.DeserializeParams<SessionCreateRequest>();

        Assert.NotNull(payload);
        Assert.Equal("req-1", request.Id);
        Assert.Equal(SessionMode.Code, payload!.Mode);
        Assert.Equal("C:\\Projects\\NexCode", payload.ProjectPath);
    }
}
