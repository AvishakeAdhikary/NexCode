using System;
using System.Security.Cryptography;
using System.Text;
using NexCode.Remote.Security;

namespace NexCode.Cli.Tests.Remote;

public sealed class EcdhAesGcmCipherTests
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("nexcode-remote/test");
    private static readonly byte[] InfoClient = Encoding.UTF8.GetBytes("nexcode-remote/client");
    private static readonly byte[] InfoServer = Encoding.UTF8.GetBytes("nexcode-remote/server");

    [Fact]
    public void Roundtrip_EncryptThenDecrypt_RecoversPlaintext()
    {
        var alice = X25519.Generate();
        var bob = X25519.Generate();

        using var aliceCipher = EcdhAesGcmCipher.DeriveFromHandshake(alice.PrivateKey, bob.PublicKey, Salt, InfoClient);
        using var bobCipher = EcdhAesGcmCipher.DeriveFromHandshake(bob.PrivateKey, alice.PublicKey, Salt, InfoClient);

        var payload = Encoding.UTF8.GetBytes("hello nexcode-remote");
        var ciphertext = aliceCipher.Encrypt(1, payload, out var nonce, out var tag);
        var decrypted = bobCipher.Decrypt(1, nonce, ciphertext, tag);

        Assert.Equal(payload, decrypted);
    }

    [Fact]
    public void TamperedCiphertext_FailsAesGcmAuthentication()
    {
        var alice = X25519.Generate();
        var bob = X25519.Generate();

        using var aliceCipher = EcdhAesGcmCipher.DeriveFromHandshake(alice.PrivateKey, bob.PublicKey, Salt, InfoClient);
        using var bobCipher = EcdhAesGcmCipher.DeriveFromHandshake(bob.PrivateKey, alice.PublicKey, Salt, InfoClient);

        var payload = Encoding.UTF8.GetBytes("authenticate me");
        var ciphertext = aliceCipher.Encrypt(2, payload, out var nonce, out var tag);
        ciphertext[0] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => bobCipher.Decrypt(2, nonce, ciphertext, tag));
    }

    [Fact]
    public void DifferentInfoLabels_ProduceDifferentKeys()
    {
        var alice = X25519.Generate();
        var bob = X25519.Generate();

        using var aliceClient = EcdhAesGcmCipher.DeriveFromHandshake(alice.PrivateKey, bob.PublicKey, Salt, InfoClient);
        using var bobServer = EcdhAesGcmCipher.DeriveFromHandshake(bob.PrivateKey, alice.PublicKey, Salt, InfoServer);

        var payload = Encoding.UTF8.GetBytes("split-direction");
        var ciphertext = aliceClient.Encrypt(7, payload, out var nonce, out var tag);

        Assert.Throws<CryptographicException>(() => bobServer.Decrypt(7, nonce, ciphertext, tag));
    }
}
