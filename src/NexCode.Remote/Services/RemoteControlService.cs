using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexCode.Remote.Security;

namespace NexCode.Remote.Services;

/// <summary>
/// Spec §23 — full RemoteControl gRPC implementation.
///
/// 1. <see cref="ExchangeKeys"/> handles a single-shot ECDH probe.
/// 2. <see cref="OpenSession"/> upgrades a bidi stream into an encrypted
///    transport: the first ClientEvent must carry a KeyExchange request, after
///    which both sides exchange EncryptedFrames keyed by HKDF(shared_secret).
/// 3. Frames are dispatched to <see cref="IRemoteSessionAdapter"/> for the local
///    SessionTurnService bridge — this adapter is wired up in tests and the
///    eventual cloud entry point.
/// </summary>
[Authorize(Policy = "RequireMsalJwt")]
public sealed class RemoteControlService(
    ILogger<RemoteControlService> logger,
    IOptions<RemoteHostOptions> options,
    IRemoteSessionAdapter sessionAdapter,
    IRemoteSigningKeyProvider signingKeyProvider) : RemoteControl.RemoteControlBase
{
    private static readonly byte[] HkdfSalt = Encoding.UTF8.GetBytes("nexcode-remote/v1");
    private static readonly byte[] HkdfInfoServer = Encoding.UTF8.GetBytes("nexcode-remote/server");
    private static readonly byte[] HkdfInfoClient = Encoding.UTF8.GetBytes("nexcode-remote/client");

    public override Task<RemoteHealthReply> GetRemoteHealth(RemoteHealthRequest request, ServerCallContext context)
    {
        logger.LogInformation("Remote health requested from {Peer}", context.Peer);
        return Task.FromResult(new RemoteHealthReply
        {
            ServiceName = "nexcode-remote",
            State = "healthy",
            Version = options.Value.Version,
            Transport = options.Value.Transport,
            Message = "Remote execution is online and ready for ECDH key exchange."
        });
    }

    public override Task<KeyExchangeReply> ExchangeKeys(KeyExchangeRequest request, ServerCallContext context)
    {
        if (request.EphemeralPublicKey is null || request.EphemeralPublicKey.Length != X25519.PointSize)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ephemeral_public_key must be 32 bytes."));
        }

        var serverPair = X25519.Generate();
        var signature = SignHandshake(request.EphemeralPublicKey.ToByteArray(), serverPair.PublicKey);

        return Task.FromResult(new KeyExchangeReply
        {
            EphemeralPublicKey = ByteString.CopyFrom(serverPair.PublicKey),
            ServerSignature = ByteString.CopyFrom(signature)
        });
    }

    public override async Task OpenSession(
        IAsyncStreamReader<ClientEvent> requestStream,
        IServerStreamWriter<ServerEvent> responseStream,
        ServerCallContext context)
    {
        if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Client closed stream before sending KeyExchange."));
        }

        var first = requestStream.Current;
        if (first.EventCase != ClientEvent.EventOneofCase.KeyExchange)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "First event must be a KeyExchange request."));
        }

        var clientPublic = first.KeyExchange.EphemeralPublicKey.ToByteArray();
        if (clientPublic.Length != X25519.PointSize)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ephemeral_public_key must be 32 bytes."));
        }

        var serverPair = X25519.Generate();
        var signature = SignHandshake(clientPublic, serverPair.PublicKey);
        await responseStream.WriteAsync(new ServerEvent
        {
            KeyExchange = new KeyExchangeReply
            {
                EphemeralPublicKey = ByteString.CopyFrom(serverPair.PublicKey),
                ServerSignature = ByteString.CopyFrom(signature)
            }
        }).ConfigureAwait(false);

        using var inboundCipher = EcdhAesGcmCipher.DeriveFromHandshake(serverPair.PrivateKey, clientPublic, HkdfSalt, HkdfInfoClient);
        using var outboundCipher = EcdhAesGcmCipher.DeriveFromHandshake(serverPair.PrivateKey, clientPublic, HkdfSalt, HkdfInfoServer);

        // sequence numbers per direction.
        var clientSequence = new SequenceWindow();
        ulong serverSequence = 0;
        var sessionId = string.IsNullOrWhiteSpace(first.KeyExchange.SessionId) ? Guid.NewGuid().ToString("N") : first.KeyExchange.SessionId;
        logger.LogInformation("Opened encrypted session {SessionId} from {Peer}", sessionId, context.Peer);

        await using var session = await sessionAdapter.OpenAsync(sessionId, async payload =>
        {
            var seq = Interlocked.Increment(ref serverSequence);
            var ciphertext = outboundCipher.Encrypt(seq, payload, out var nonce, out var tag);
            await responseStream.WriteAsync(new ServerEvent
            {
                EncryptedFrame = new EncryptedFrame
                {
                    Sequence = seq,
                    Nonce = ByteString.CopyFrom(nonce),
                    Ciphertext = ByteString.CopyFrom(ciphertext),
                    AuthTag = ByteString.CopyFrom(tag)
                }
            }).ConfigureAwait(false);
        }, context.CancellationToken).ConfigureAwait(false);

        while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            var evt = requestStream.Current;
            if (evt.EventCase != ClientEvent.EventOneofCase.EncryptedFrame)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Only EncryptedFrame is permitted after handshake."));
            }

            var frame = evt.EncryptedFrame;
            if (!clientSequence.TryAdvance(frame.Sequence))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Replay or out-of-order frame detected."));
            }

            byte[] plaintext;
            try
            {
                plaintext = inboundCipher.Decrypt(
                    frame.Sequence,
                    frame.Nonce.Span,
                    frame.Ciphertext.Span,
                    frame.AuthTag.Span);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Frame {Sequence} failed AES-GCM auth on session {SessionId}", frame.Sequence, sessionId);
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Frame failed authentication."));
            }

            await session.PushClientFrameAsync(plaintext, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private byte[] SignHandshake(byte[] clientPublic, byte[] serverPublic)
    {
        var key = signingKeyProvider.GetCurrentKey();
        using var hmac = new HMACSHA256(key);
        hmac.AppendData(clientPublic);
        hmac.AppendData(serverPublic);
        return hmac.GetHashAndReset();
    }
}

internal static class HmacExtensions
{
    public static void AppendData(this HMACSHA256 hmac, byte[] data) =>
        hmac.TransformBlock(data, 0, data.Length, null, 0);

    public static byte[] GetHashAndReset(this HMACSHA256 hmac)
    {
        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = hmac.Hash ?? Array.Empty<byte>();
        hmac.Initialize();
        return hash;
    }
}

/// <summary>Tracks the highest-seen sequence number to detect replays without an unbounded window.</summary>
internal sealed class SequenceWindow
{
    private ulong _max;

    public bool TryAdvance(ulong sequence)
    {
        // Strictly increasing windows are sufficient for the bidi stream design.
        if (sequence == 0 || sequence <= _max)
        {
            return false;
        }

        _max = sequence;
        return true;
    }
}
