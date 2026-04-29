using System;
using System.IO;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.IO.Pem;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Generates new SSH key pairs (RSA-4096 or Ed25519) using BouncyCastle, returning a
/// PKCS#8 PEM private key and an OpenSSH-format public key. The Settings agent owns
/// the surface UI; this service only does crypto + serialization.
/// </summary>
public sealed class SshKeysGenerator
{
    public SshKeyPair Generate(SshKeyAlgorithm algorithm, string comment = "nexcode")
    {
        return algorithm switch
        {
            SshKeyAlgorithm.Rsa4096 => GenerateRsa(4096, comment),
            SshKeyAlgorithm.Ed25519 => GenerateEd25519(comment),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }

    private static SshKeyPair GenerateRsa(int keyBits, string comment)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new RsaKeyGenerationParameters(
            Org.BouncyCastle.Math.BigInteger.ValueOf(0x10001),
            new SecureRandom(),
            keyBits,
            64));

        var keyPair = generator.GenerateKeyPair();
        var privatePem = ToPkcs8Pem(keyPair.Private);
        var publicSsh = BuildOpenSshRsaPublic((RsaKeyParameters)keyPair.Public, comment);
        return new SshKeyPair("ssh-rsa", privatePem, publicSsh);
    }

    private static SshKeyPair GenerateEd25519(string comment)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        var privatePem = ToPkcs8Pem(keyPair.Private);
        var publicSsh = BuildOpenSshEd25519Public((Ed25519PublicKeyParameters)keyPair.Public, comment);
        return new SshKeyPair("ssh-ed25519", privatePem, publicSsh);
    }

    private static string ToPkcs8Pem(AsymmetricKeyParameter privateKey)
    {
        var info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey);
        var encoded = info.GetEncoded();
        using var sw = new StringWriter();
        var writer = new PemWriter(sw);
        writer.WriteObject(new PemObject("PRIVATE KEY", encoded));
        writer.Writer.Flush();
        return sw.ToString();
    }

    private static string BuildOpenSshRsaPublic(RsaKeyParameters publicKey, string comment)
    {
        using var ms = new MemoryStream();
        WriteSshString(ms, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteSshMpInt(ms, publicKey.Exponent.ToByteArray());
        WriteSshMpInt(ms, publicKey.Modulus.ToByteArray());
        var b64 = Convert.ToBase64String(ms.ToArray());
        return $"ssh-rsa {b64} {comment}".Trim();
    }

    private static string BuildOpenSshEd25519Public(Ed25519PublicKeyParameters publicKey, string comment)
    {
        using var ms = new MemoryStream();
        WriteSshString(ms, Encoding.ASCII.GetBytes("ssh-ed25519"));
        WriteSshString(ms, publicKey.GetEncoded());
        var b64 = Convert.ToBase64String(ms.ToArray());
        return $"ssh-ed25519 {b64} {comment}".Trim();
    }

    private static void WriteSshString(Stream stream, byte[] data)
    {
        var lengthBytes = BitConverter.GetBytes((uint)data.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(lengthBytes);
        }
        stream.Write(lengthBytes, 0, 4);
        stream.Write(data, 0, data.Length);
    }

    private static void WriteSshMpInt(Stream stream, byte[] value)
    {
        if (value.Length > 0 && (value[0] & 0x80) != 0)
        {
            var padded = new byte[value.Length + 1];
            Buffer.BlockCopy(value, 0, padded, 1, value.Length);
            WriteSshString(stream, padded);
        }
        else
        {
            WriteSshString(stream, value);
        }
    }

    public static void WriteToDisk(SshKeyPair pair, string privateKeyPath, string publicKeyPath)
    {
        File.WriteAllText(privateKeyPath, pair.PrivateKeyPem);
        File.WriteAllText(publicKeyPath, pair.PublicKeyOpenSsh);
        try
        {
            var perms = File.GetUnixFileMode(privateKeyPath);
            _ = perms;
            File.SetUnixFileMode(privateKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // POSIX permission helper is best-effort on Windows.
        }
    }
}

public enum SshKeyAlgorithm { Rsa4096, Ed25519 }

public sealed record SshKeyPair(string Algorithm, string PrivateKeyPem, string PublicKeyOpenSsh);
