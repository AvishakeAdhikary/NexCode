using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexCode.Remote.Services;

/// <summary>
/// Spec §23.2 — bridge between the encrypted gRPC stream and the local
/// SessionTurnService-equivalent that actually executes sessions.
/// nexcode-remote ships a minimal echo implementation; cloud and on-prem
/// deployments substitute their own adapter to forward turns into the agent
/// loop. Keeping this an interface lets the gRPC service stay transport-only.
/// </summary>
public interface IRemoteSessionAdapter
{
    Task<IRemoteSession> OpenAsync(string sessionId, Func<byte[], Task> sendToClient, CancellationToken cancellationToken);
}

public interface IRemoteSession : IAsyncDisposable
{
    Task PushClientFrameAsync(byte[] plaintext, CancellationToken cancellationToken);
}

public interface IRemoteSigningKeyProvider
{
    byte[] GetCurrentKey();
}
