using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexCode.Remote;
using NexCode.Remote.Security;

namespace NexCode.Service.Remote;

/// <summary>
/// Spec §23 — gRPC client used by NexCode.Service when a session's
/// <see cref="NexCode.Shared.Models.ExecutionMode"/> is Remote (or Cloud, with
/// the cloud endpoint substituted). Connects with mTLS + Bearer JWT, performs
/// the X25519 handshake, then keeps an encrypted bidirectional stream open.
/// </summary>
public sealed class RemoteSessionClient(
    ILogger<RemoteSessionClient> logger,
    IOptions<RemoteClientOptions> options) : IAsyncDisposable
{
    private static readonly byte[] HkdfSalt = Encoding.UTF8.GetBytes("nexcode-remote/v1");
    private static readonly byte[] HkdfInfoServer = Encoding.UTF8.GetBytes("nexcode-remote/server");
    private static readonly byte[] HkdfInfoClient = Encoding.UTF8.GetBytes("nexcode-remote/client");

    private GrpcChannel? _channel;
    private RemoteControl.RemoteControlClient? _client;
    private AsyncDuplexStreamingCall<ClientEvent, ServerEvent>? _stream;
    private EcdhAesGcmCipher? _outboundCipher;
    private EcdhAesGcmCipher? _inboundCipher;
    private ulong _outboundSequence;
    private ulong _inboundSequence;

    public bool IsConnected => _stream is not null;

    public string? Endpoint => options.Value.Endpoint;

    public async Task<RemoteHealthReply> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetRemoteHealthAsync(new RemoteHealthRequest(), cancellationToken: cancellationToken);
    }

    public async Task ConnectAsync(string sessionId, Func<byte[], Task> onServerFrame, CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);

        var clientPair = X25519.Generate();
        _stream = client.OpenSession(cancellationToken: cancellationToken);

        await _stream.RequestStream.WriteAsync(new ClientEvent
        {
            KeyExchange = new KeyExchangeRequest
            {
                EphemeralPublicKey = ByteString.CopyFrom(clientPair.PublicKey),
                SessionId = sessionId
            }
        }).ConfigureAwait(false);

        if (!await _stream.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Remote endpoint closed the stream before handshake.");
        }

        var first = _stream.ResponseStream.Current;
        if (first.EventCase != ServerEvent.EventOneofCase.KeyExchange)
        {
            throw new InvalidOperationException("Remote endpoint did not return KeyExchange first.");
        }

        var serverPublic = first.KeyExchange.EphemeralPublicKey.ToByteArray();
        _outboundCipher = EcdhAesGcmCipher.DeriveFromHandshake(clientPair.PrivateKey, serverPublic, HkdfSalt, HkdfInfoClient);
        _inboundCipher = EcdhAesGcmCipher.DeriveFromHandshake(clientPair.PrivateKey, serverPublic, HkdfSalt, HkdfInfoServer);

        // Run a reader pump to deliver decrypted frames to the consumer.
        _ = Task.Run(() => ReadPumpAsync(onServerFrame, cancellationToken));
        logger.LogInformation("Remote session {SessionId} connected to {Endpoint}.", sessionId, options.Value.Endpoint);
    }

    public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (_stream is null || _outboundCipher is null)
        {
            throw new InvalidOperationException("Remote session is not connected.");
        }

        var seq = Interlocked.Increment(ref _outboundSequence);
        var ciphertext = _outboundCipher.Encrypt(seq, payload, out var nonce, out var tag);
        await _stream.RequestStream.WriteAsync(new ClientEvent
        {
            EncryptedFrame = new EncryptedFrame
            {
                Sequence = seq,
                Nonce = ByteString.CopyFrom(nonce),
                Ciphertext = ByteString.CopyFrom(ciphertext),
                AuthTag = ByteString.CopyFrom(tag)
            }
        }).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        if (_stream is not null)
        {
            try { await _stream.RequestStream.CompleteAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
            _stream.Dispose();
            _stream = null;
        }

        _outboundCipher?.Dispose();
        _inboundCipher?.Dispose();
        _outboundCipher = null;
        _inboundCipher = null;
        _outboundSequence = 0;
        _inboundSequence = 0;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        if (_channel is not null)
        {
            await _channel.ShutdownAsync().ConfigureAwait(false);
            _channel.Dispose();
            _channel = null;
        }
    }

    private async Task ReadPumpAsync(Func<byte[], Task> onServerFrame, CancellationToken cancellationToken)
    {
        try
        {
            if (_stream is null)
            {
                return;
            }

            while (await _stream.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                var evt = _stream.ResponseStream.Current;
                if (evt.EventCase != ServerEvent.EventOneofCase.EncryptedFrame || _inboundCipher is null)
                {
                    continue;
                }

                var frame = evt.EncryptedFrame;
                var plaintext = _inboundCipher.Decrypt(frame.Sequence, frame.Nonce.Span, frame.Ciphertext.Span, frame.AuthTag.Span);
                Interlocked.Exchange(ref _inboundSequence, frame.Sequence);
                await onServerFrame(plaintext).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote read pump terminated.");
        }
    }

    private async Task<RemoteControl.RemoteControlClient> EnsureClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        var endpoint = options.Value.Endpoint
            ?? throw new InvalidOperationException("RemoteClient endpoint is not configured. Set RemoteClient:Endpoint.");

        var handler = new HttpClientHandler();
        if (TryLoadClientCert(out var clientCert) && clientCert is not null)
        {
            handler.ClientCertificates.Add(clientCert);
        }

        if (options.Value.AllowInsecureDevelopmentChannel)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var httpClient = new HttpClient(handler);
        if (!string.IsNullOrWhiteSpace(options.Value.BearerToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Value.BearerToken);
        }

        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpClient = httpClient });
        _client = new RemoteControl.RemoteControlClient(_channel);
        await Task.CompletedTask;
        return _client;
    }

    private static bool TryLoadClientCert(out X509Certificate2? certificate)
    {
        certificate = null;
        var path = Environment.GetEnvironmentVariable("NEXCODE_REMOTE_CLIENT_CERT_PFX");
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return false;
        }

        var password = Environment.GetEnvironmentVariable("NEXCODE_REMOTE_CLIENT_CERT_PASSWORD");
        certificate = new X509Certificate2(path, password, X509KeyStorageFlags.MachineKeySet);
        return true;
    }
}

public sealed class RemoteClientOptions
{
    public const string SectionName = "RemoteClient";

    public string? Endpoint { get; set; }

    public string? BearerToken { get; set; }

    public bool AllowInsecureDevelopmentChannel { get; set; }
}
