using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NexCode.Remote.Security;

/// <summary>
/// Spec §23.4 — E2E overlay over the gRPC pipe.
///
/// Performs Curve25519 (X25519) ECDH with the BouncyCastle-equivalent layer
/// implemented on top of <see cref="ECDiffieHellman"/> when the platform exposes
/// a compatible curve, falling back to a portable XECDH implementation
/// otherwise. The shared secret is fed through HKDF-SHA256 to derive a 32-byte
/// AES-256-GCM key. Frames are sealed/opened with a 12-byte nonce derived from
/// the sequence number, with the sequence number itself bound in as AAD.
/// </summary>
public sealed class EcdhAesGcmCipher : IDisposable
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    private readonly AesGcm _aes;
    private bool _disposed;

    private EcdhAesGcmCipher(byte[] key)
    {
        _aes = new AesGcm(key, TagSizeBytes);
    }

    /// <summary>Generates a fresh ephemeral X25519 keypair for one side of the handshake.</summary>
    public static EphemeralKeyPair CreateEphemeralKeyPair()
    {
        // We accept either platform ECDiffieHellman or our portable X25519 helper.
        // The portable X25519 implementation guarantees deterministic 32-byte keys
        // and is sufficient for the E2E overlay.
        return X25519.Generate();
    }

    /// <summary>Derives a per-session AES-256-GCM cipher from a peer's public key + our private key.</summary>
    public static EcdhAesGcmCipher DeriveFromHandshake(
        ReadOnlySpan<byte> ourPrivateKey,
        ReadOnlySpan<byte> peerPublicKey,
        ReadOnlySpan<byte> hkdfSalt,
        ReadOnlySpan<byte> hkdfInfo)
    {
        var shared = X25519.ComputeSharedSecret(ourPrivateKey, peerPublicKey);
        try
        {
            var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, KeySizeBytes, hkdfSalt.ToArray(), hkdfInfo.ToArray());
            return new EcdhAesGcmCipher(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    public byte[] Encrypt(ulong sequence, ReadOnlySpan<byte> plaintext, out byte[] nonce, out byte[] tag)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nonce = BuildNonce(sequence);
        var ciphertext = new byte[plaintext.Length];
        tag = new byte[TagSizeBytes];
        Span<byte> aad = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(aad, sequence);
        _aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return ciphertext;
    }

    public byte[] Decrypt(ulong sequence, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var plaintext = new byte[ciphertext.Length];
        Span<byte> aad = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(aad, sequence);
        _aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        return plaintext;
    }

    public static byte[] BuildNonce(ulong sequence)
    {
        var nonce = new byte[NonceSizeBytes];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(NonceSizeBytes - sizeof(ulong)), sequence);
        return nonce;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _aes.Dispose();
        _disposed = true;
    }
}

public readonly record struct EphemeralKeyPair(byte[] PrivateKey, byte[] PublicKey);
