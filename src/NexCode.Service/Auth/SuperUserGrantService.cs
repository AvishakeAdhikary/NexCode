using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexCode.Shared.Json;

namespace NexCode.Service.Auth;

[SupportedOSPlatform("windows")]
public sealed class SuperUserGrantService(
    IOptions<SuperUserGrantOptions> options,
    ILogger<SuperUserGrantService> logger) : ISuperUserGrantService
{
    public async Task<bool> HasValidGrantAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(options.Value.PublicKeyPem))
        {
            return false;
        }

        if (!File.Exists(GetGrantFilePath()))
        {
            return false;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(GetGrantFilePath(), cancellationToken);
            var payloadBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            var grant = JsonSerializer.Deserialize<SignedSuperUserGrant>(payloadBytes, JsonSerialization.Options);
            if (grant is null || grant.Version != SignedSuperUserGrant.CurrentVersion)
            {
                return false;
            }

            if (grant.ExpiresAt is not null && grant.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            var normalizedEmailHash = ComputeEmailHash(email);
            if (!FixedTimeEquals(normalizedEmailHash, grant.EmailHash))
            {
                return false;
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(options.Value.PublicKeyPem);

            var signatureBytes = Convert.FromBase64String(grant.SignatureBase64);
            return rsa.VerifyData(
                GetSignedBytes(grant),
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to validate the sealed superuser grant.");
            return false;
        }
    }

    public static string ComputeEmailHash(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToBase64String(bytes);
    }

    public string GetGrantFilePath()
    {
        var authRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexCode",
            "Auth");

        return Path.Combine(authRoot, options.Value.GrantFileName);
    }

    private static byte[] GetSignedBytes(SignedSuperUserGrant grant)
    {
        var content = string.Join(
            "\n",
            grant.Version,
            grant.EmailHash,
            grant.IssuedAt.UtcDateTime.ToString("O"),
            grant.ExpiresAt?.UtcDateTime.ToString("O") ?? string.Empty);

        return Encoding.UTF8.GetBytes(content);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed record SignedSuperUserGrant(
    string Version,
    string EmailHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    string SignatureBase64)
{
    public const string CurrentVersion = "1";
}
