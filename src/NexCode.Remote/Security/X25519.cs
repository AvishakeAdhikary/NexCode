using System;
using System.Security.Cryptography;

namespace NexCode.Remote.Security;

/// <summary>
/// Minimal RFC 7748 X25519 implementation used by the E2E overlay.
///
/// This is intentionally small and self-contained: the .NET cryptographic
/// surface in net9.0 only exposes Curve25519 ECDH on certain platforms, so
/// shipping a managed reference implementation lets nexcode-remote run
/// identically on Windows, Linux containers and the eventual Cloud endpoint.
/// </summary>
public static class X25519
{
    public const int ScalarSize = 32;
    public const int PointSize = 32;

    private const int FieldSize = 32;
    private static readonly byte[] BasePoint = { 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public static EphemeralKeyPair Generate()
    {
        var privateKey = RandomNumberGenerator.GetBytes(ScalarSize);
        ClampScalar(privateKey);
        var publicKey = ScalarMult(privateKey, BasePoint);
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

        var clamped = privateKey.ToArray();
        ClampScalar(clamped);
        return ScalarMult(clamped, peerPublicKey.ToArray());
    }

    private static void ClampScalar(byte[] scalar)
    {
        scalar[0] &= 248;
        scalar[31] &= 127;
        scalar[31] |= 64;
    }

    // RFC 7748 §5 — scalar multiplication on Curve25519.
    private static byte[] ScalarMult(byte[] scalar, byte[] point)
    {
        Span<long> x1 = stackalloc long[16];
        Span<long> x2 = stackalloc long[16];
        Span<long> z2 = stackalloc long[16];
        Span<long> x3 = stackalloc long[16];
        Span<long> z3 = stackalloc long[16];
        Span<long> tmpA = stackalloc long[16];
        Span<long> tmpB = stackalloc long[16];

        Unpack25519(x1, point);
        x2[0] = 1;
        x3[0] = 1;
        x1.CopyTo(x3);
        z3[0] = 1;

        var swap = 0;
        for (var i = 254; i >= 0; i--)
        {
            var bit = (scalar[i / 8] >> (i & 7)) & 1;
            swap ^= bit;
            CSwap(x2, x3, swap);
            CSwap(z2, z3, swap);
            swap = bit;

            FieldAdd(tmpA, x3, z3);
            FieldSub(x3, x3, z3);
            FieldAdd(z3, x2, z2);
            FieldSub(z2, x2, z2);
            FieldMul(x3, tmpA, z2);
            FieldMul(z3, x3, z3);
            FieldSquare(tmpA, z3);
            FieldSquare(tmpB, z2);
            FieldMul(x2, tmpA, tmpB);
            FieldSub(tmpB, tmpA, tmpB);
            FieldMulInt(z2, tmpB, 121665);
            FieldAdd(z2, z2, tmpA);
            FieldMul(z2, tmpB, z2);
            FieldMul(z3, x3, z3);
            FieldSquare(x3, x3);
            FieldAdd(z3, z3, z3);
            FieldSquare(tmpA, x3);
            FieldSub(tmpB, x3, z2);
            FieldMul(z2, tmpA, z3);
            FieldMul(x3, x1, tmpB);
        }
        CSwap(x2, x3, swap);
        CSwap(z2, z3, swap);

        FieldInvert(z2, z2);
        FieldMul(x2, x2, z2);
        var result = new byte[FieldSize];
        Pack25519(result, x2);
        return result;
    }

    private static void Unpack25519(Span<long> output, ReadOnlySpan<byte> input)
    {
        for (var i = 0; i < 16; i++)
        {
            output[i] = input[2 * i] | ((long)input[2 * i + 1] << 8);
        }
        output[15] &= 0x7FFF;
    }

    private static void Pack25519(Span<byte> output, Span<long> input)
    {
        Span<long> tmp = stackalloc long[16];
        input.CopyTo(tmp);
        Carry25519(tmp);
        Carry25519(tmp);
        Carry25519(tmp);
        for (var i = 0; i < 2; i++)
        {
            Span<long> m = stackalloc long[16];
            m[0] = tmp[0] - 0xFFED;
            for (var j = 1; j < 15; j++)
            {
                m[j] = tmp[j] - 0xFFFF - ((m[j - 1] >> 16) & 1);
                m[j - 1] &= 0xFFFF;
            }
            m[15] = tmp[15] - 0x7FFF - ((m[14] >> 16) & 1);
            var b = (m[15] >> 16) & 1;
            m[14] &= 0xFFFF;
            CSwap(tmp, m, 1 - (int)b);
        }
        for (var i = 0; i < 16; i++)
        {
            output[2 * i] = (byte)(tmp[i] & 0xFF);
            output[2 * i + 1] = (byte)((tmp[i] >> 8) & 0xFF);
        }
    }

    private static void Carry25519(Span<long> input)
    {
        for (var i = 0; i < 16; i++)
        {
            input[i] += 1L << 16;
            var carry = input[i] >> 16;
            input[(i + 1) * (i < 15 ? 1 : 0)] += carry - 1 + 37 * (carry - 1) * (i == 15 ? 1 : 0);
            input[i] -= carry << 16;
        }
    }

    private static void FieldAdd(Span<long> result, Span<long> a, Span<long> b)
    {
        for (var i = 0; i < 16; i++)
        {
            result[i] = a[i] + b[i];
        }
    }

    private static void FieldSub(Span<long> result, Span<long> a, Span<long> b)
    {
        for (var i = 0; i < 16; i++)
        {
            result[i] = a[i] - b[i];
        }
    }

    private static void FieldMul(Span<long> result, Span<long> a, Span<long> b)
    {
        Span<long> t = stackalloc long[31];
        for (var i = 0; i < 16; i++)
        {
            for (var j = 0; j < 16; j++)
            {
                t[i + j] += a[i] * b[j];
            }
        }
        for (var i = 0; i < 15; i++)
        {
            t[i] += 38 * t[i + 16];
        }
        for (var i = 0; i < 16; i++)
        {
            result[i] = t[i];
        }
        Carry25519(result);
        Carry25519(result);
    }

    private static void FieldSquare(Span<long> result, Span<long> a) => FieldMul(result, a, a);

    private static void FieldMulInt(Span<long> result, Span<long> a, int s)
    {
        for (var i = 0; i < 16; i++)
        {
            result[i] = a[i] * s;
        }
        Carry25519(result);
        Carry25519(result);
    }

    private static void FieldInvert(Span<long> result, Span<long> a)
    {
        Span<long> c = stackalloc long[16];
        a.CopyTo(c);
        for (var i = 253; i >= 0; i--)
        {
            FieldSquare(c, c);
            if (i != 2 && i != 4)
            {
                FieldMul(c, c, a);
            }
        }
        c.CopyTo(result);
    }

    private static void CSwap(Span<long> a, Span<long> b, int swap)
    {
        var mask = -(long)swap;
        for (var i = 0; i < 16; i++)
        {
            var t = mask & (a[i] ^ b[i]);
            a[i] ^= t;
            b[i] ^= t;
        }
    }
}
