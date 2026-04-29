using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Models;

namespace NexCode.Service.Remote;

/// <summary>
/// Routes session messages either through the local agent loop
/// (<see cref="SessionTurnService"/>) or through <see cref="RemoteSessionClient"/>
/// based on <see cref="SessionRuntimeState.Request"/> ExecutionMode. Cloud
/// execution reuses the same code path as Remote, with a different endpoint
/// resolved by <see cref="Cloud.CloudExecutionRouter"/>.
/// </summary>
public sealed class RemoteSessionRouter(
    ILogger<RemoteSessionRouter> logger,
    SessionTurnService localTurnService,
    RemoteSessionClient remoteSessionClient)
{
    public async Task DispatchAsync(
        SessionRuntimeState session,
        Guid assistantMessageId,
        string userContent,
        CancellationToken cancellationToken)
    {
        switch (session.Request.ExecutionMode)
        {
            case ExecutionMode.Local:
                await localTurnService.ProcessTurnAsync(session, assistantMessageId, userContent, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case ExecutionMode.Remote:
            case ExecutionMode.Cloud:
                logger.LogInformation(
                    "Routing session {SessionId} via remote endpoint {Endpoint}.",
                    session.SessionId,
                    remoteSessionClient.Endpoint);
                await DispatchRemoteAsync(session, userContent, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new InvalidOperationException($"Unknown execution mode {session.Request.ExecutionMode}.");
        }
    }

    private async Task DispatchRemoteAsync(SessionRuntimeState session, string userContent, CancellationToken cancellationToken)
    {
        if (!remoteSessionClient.IsConnected)
        {
            await remoteSessionClient.ConnectAsync(
                session.SessionId.ToString("N"),
                serverFrame =>
                {
                    logger.LogDebug("Received {Bytes} bytes from remote.", serverFrame.Length);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
        }

        var payload = System.Text.Encoding.UTF8.GetBytes(userContent);
        await remoteSessionClient.SendAsync(payload, cancellationToken).ConfigureAwait(false);
    }
}
