using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NexCode.Remote.Services;

/// <summary>
/// Default in-process adapter used until the full agent-loop bridge ships in a
/// later slice. Echoes client payloads back so contract tests can verify that
/// the encrypted transport is wired end-to-end.
/// </summary>
public sealed class EchoRemoteSessionAdapter(ILogger<EchoRemoteSessionAdapter> logger) : IRemoteSessionAdapter
{
    public Task<IRemoteSession> OpenAsync(string sessionId, Func<byte[], Task> sendToClient, CancellationToken cancellationToken)
    {
        logger.LogInformation("Opening echo remote session {SessionId}", sessionId);
        return Task.FromResult<IRemoteSession>(new EchoRemoteSession(sessionId, sendToClient));
    }

    private sealed class EchoRemoteSession(string sessionId, Func<byte[], Task> sendToClient) : IRemoteSession
    {
        public string SessionId { get; } = sessionId;

        public Task PushClientFrameAsync(byte[] plaintext, CancellationToken cancellationToken)
        {
            return sendToClient(plaintext);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Default signing key provider — derives a stable HMAC key from the machine's
/// configured remote signing secret env var, falling back to a randomized
/// per-process key. In production this is replaced by a key fetched from a
/// platform secret store (Azure Key Vault, etc.).
/// </summary>
public sealed class EnvRemoteSigningKeyProvider : IRemoteSigningKeyProvider
{
    public const string SigningKeyEnv = "NEXCODE_REMOTE_SIGNING_KEY";

    private readonly byte[] _key;

    public EnvRemoteSigningKeyProvider()
    {
        var raw = Environment.GetEnvironmentVariable(SigningKeyEnv);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            _key = System.Text.Encoding.UTF8.GetBytes(raw);
        }
        else
        {
            _key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        }
    }

    public byte[] GetCurrentKey() => _key;
}
