using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace NexCode.Data.Storage;

/// <summary>
/// Default Windows-only key provider. Implements the spec §2.3 + Appendix A derivation:
/// <list type="number">
///   <item>A 32-byte cryptographically random salt is generated on first run, DPAPI-protected
///         (CurrentUser scope) and stored at <c>%LocalAppData%\NexCode\Auth\db.salt</c>.
///         The salt does not need to be secret; storing it DPAPI-protected just keeps it
///         tied to the same Windows account that derives the key.</item>
///   <item>A DPAPI-protected entropy blob (<c>ProtectedData.Protect(EntropySeed, AdditionalEntropy, CurrentUser)</c>)
///         supplies the secret input to PBKDF2.</item>
///   <item>The 32-byte SQLCipher key is derived as <c>PBKDF2-HMAC-SHA256(dpapi_blob, salt, 300_000, 32)</c>.</item>
/// </list>
/// The derived key is cached in memory for the lifetime of the provider; it is never written
/// to disk. The only artifact on disk is the DPAPI-wrapped salt, which alone is useless to
/// an attacker without the user's Windows credentials.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDatabaseKeyProvider : IDatabaseKeyProvider
{
    public const int SaltLengthBytes = 32;
    public const int Pbkdf2Iterations = 300_000;

    private static readonly byte[] EntropySeed = "NexCode.DbKeyEntropy.v1"u8.ToArray();
    private static readonly byte[] AdditionalEntropy = "NexCode.DbKey.AdditionalEntropy.v1"u8.ToArray();

    private readonly string _saltFilePath;
    private readonly object _sync = new();
    private byte[]? _cachedKey;

    public DpapiDatabaseKeyProvider(string saltFilePath)
    {
        if (string.IsNullOrWhiteSpace(saltFilePath))
        {
            throw new ArgumentException("Salt file path must be provided.", nameof(saltFilePath));
        }

        _saltFilePath = saltFilePath;
    }

    public static DpapiDatabaseKeyProvider FromLocalAppData()
    {
        var saltFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexCode",
            "Auth",
            "db.salt");

        return new DpapiDatabaseKeyProvider(saltFilePath);
    }

    public byte[] GetKey()
    {
        if (_cachedKey is not null)
        {
            return _cachedKey;
        }

        lock (_sync)
        {
            if (_cachedKey is not null)
            {
                return _cachedKey;
            }

            var salt = LoadOrCreateSalt();
            var dpapiSecret = ProtectedData.Protect(
                userData: EntropySeed,
                optionalEntropy: AdditionalEntropy,
                scope: DataProtectionScope.CurrentUser);

            _cachedKey = Rfc2898DeriveBytes.Pbkdf2(
                password: dpapiSecret,
                salt: salt,
                iterations: Pbkdf2Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: IDatabaseKeyProvider.KeyLengthBytes);

            return _cachedKey;
        }
    }

    private byte[] LoadOrCreateSalt()
    {
        var directory = Path.GetDirectoryName(_saltFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(_saltFilePath))
        {
            var protectedSalt = File.ReadAllBytes(_saltFilePath);
            try
            {
                var salt = ProtectedData.Unprotect(
                    encryptedData: protectedSalt,
                    optionalEntropy: AdditionalEntropy,
                    scope: DataProtectionScope.CurrentUser);

                if (salt.Length == SaltLengthBytes)
                {
                    return salt;
                }
            }
            catch (CryptographicException)
            {
                // Salt was created under a different user / machine. We cannot read it; the
                // database it was used to encrypt is unreadable here too. Fall through to
                // generating a fresh salt — this user simply cannot decrypt the prior db,
                // which is by-design behaviour for DPAPI CurrentUser scope.
            }
        }

        var fresh = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var protectedFresh = ProtectedData.Protect(
            userData: fresh,
            optionalEntropy: AdditionalEntropy,
            scope: DataProtectionScope.CurrentUser);

        File.WriteAllBytes(_saltFilePath, protectedFresh);
        return fresh;
    }
}
