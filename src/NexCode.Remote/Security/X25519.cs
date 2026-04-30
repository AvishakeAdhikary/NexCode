using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Math.EC.Rfc7748;

namespace NexCode.Remote.Security;

/// <summary>
/// RFC 7748 X25519 implementation used by the E2E overlay.
///
/// Backed by BouncyCastle's reference X25519 to guarantee bit-for-bit identical
/// behavior across Windows, Linux containers and the eventual Cloud endpoint.
/// We previously shipped a hand-rolled scalar-multiplication routine here; that
/// implementation had a subtle reduction bug that caused round-trip handshake
/// failures, so it is replaced with the audited BouncyCastle primitive.
/// </summary>
public static class X25519
{
    public const int ScalarSize = 32;
    public const int PointSize = 32;

    public static EphemeralKeyPair Generate()
    {
        var privateKey = RandomNumberGenerator.GetBytes(ScalarSize);
        // Curve25519 clamping is performed inside BouncyCastle's ScalarMultBase.
        var publicKey = new byte[PointSize];
        Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMultBase(privateKey, 0, publicKey, 0);
        return new EphemeralKeyPair(privateKey, publicKey);
    }

    public static byte[] ComputeSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> peerPublicKey)
    {
        if (privateKey.Length != ScalarSize)
        {
            throw new ArgumentException("Private key must be 32 bytes.", nameof(privateKey));
        }

        if (peerPublicKey.Length != PointSize)
        {
            throw new ArgumentException("Public key must be 32 bytes.", nameof(peerPublicKey));
        }

        var shared = new byte[PointSize];
        Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(
            privateKey.ToArray(), 0,
            peerPublicKey.ToArray(), 0,
            shared, 0);
        return shared;
    }
}
