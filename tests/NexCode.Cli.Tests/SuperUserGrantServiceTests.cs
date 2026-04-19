using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexCode.Service.Auth;
using NexCode.Shared.Json;

namespace NexCode.Cli.Tests;

[SupportedOSPlatform("windows")]
public sealed class SuperUserGrantServiceTests
{
    [Fact]
    public async Task ValidSignedGrant_RecognizesMatchingAccount()
    {
        using var rsa = RSA.Create(2048);
        var email = "privileged@example.test";
        var issuedAt = DateTimeOffset.Parse("2026-04-19T00:00:00Z");
        var grantFileName = $"test-superuser-{Guid.NewGuid():N}.grant";
        var options = Options.Create(new SuperUserGrantOptions
        {
            GrantFileName = grantFileName,
            PublicKeyPem = rsa.ExportRSAPublicKeyPem()
        });

        var service = new SuperUserGrantService(options, NullLogger<SuperUserGrantService>.Instance);
        var grantPath = service.GetGrantFilePath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(grantPath)!);

            var grant = CreateGrant(rsa, email, issuedAt, expiresAt: issuedAt.AddDays(30));
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(grant, JsonSerialization.Options)),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(grantPath, protectedBytes);

            var isValid = await service.HasValidGrantAsync(email);

            Assert.True(isValid);
        }
        finally
        {
            if (File.Exists(grantPath))
            {
                File.Delete(grantPath);
            }
        }
    }

    [Fact]
    public async Task Grant_DoesNotMatchDifferentAccount()
    {
        using var rsa = RSA.Create(2048);
        var options = Options.Create(new SuperUserGrantOptions
        {
            GrantFileName = $"test-superuser-{Guid.NewGuid():N}.grant",
            PublicKeyPem = rsa.ExportRSAPublicKeyPem()
        });

        var service = new SuperUserGrantService(options, NullLogger<SuperUserGrantService>.Instance);
        var grantPath = service.GetGrantFilePath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(grantPath)!);

            var grant = CreateGrant(
                rsa,
                "privileged@example.test",
                issuedAt: DateTimeOffset.Parse("2026-04-19T00:00:00Z"),
                expiresAt: null);
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(grant, JsonSerialization.Options)),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(grantPath, protectedBytes);

            var isValid = await service.HasValidGrantAsync("different@example.test");

            Assert.False(isValid);
        }
        finally
        {
            if (File.Exists(grantPath))
            {
                File.Delete(grantPath);
            }
        }
    }

    private static SignedSuperUserGrant CreateGrant(
        RSA rsa,
        string email,
        DateTimeOffset issuedAt,
        DateTimeOffset? expiresAt)
    {
        var emailHash = SuperUserGrantService.ComputeEmailHash(email);
        var signable = string.Join(
            "\n",
            SignedSuperUserGrant.CurrentVersion,
            emailHash,
            issuedAt.UtcDateTime.ToString("O"),
            expiresAt?.UtcDateTime.ToString("O") ?? string.Empty);
        var signatureBytes = rsa.SignData(
            Encoding.UTF8.GetBytes(signable),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return new SignedSuperUserGrant(
            Version: SignedSuperUserGrant.CurrentVersion,
            EmailHash: emailHash,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            SignatureBase64: Convert.ToBase64String(signatureBytes));
    }
}
