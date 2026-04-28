using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace NexCode.Service.Providers;

/// <summary>
/// DPAPI-based protector for per-provider API keys at the column level. The database itself
/// is already encrypted via SQLCipher (Slice 0011 phase 2); this layer adds an additional
/// per-user envelope so that even a memory-dumped row leaks ciphertext rather than plaintext.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProviderApiKeyProtector
{
    private static readonly byte[] EntropySeed =
        "NexCode.ProviderApiKey.v1"u8.ToArray();

    public string Protect(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return string.Empty;
        }

        var protectedBytes = ProtectedData.Protect(
            userData: Encoding.UTF8.GetBytes(apiKey),
            optionalEntropy: EntropySeed,
            scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(encrypted);
            var clear = ProtectedData.Unprotect(
                encryptedData: protectedBytes,
                optionalEntropy: EntropySeed,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (Exception)
        {
            // Encrypted under a different user, machine, or seed. Treat as missing.
            return string.Empty;
        }
    }
}
