using System.Security.Cryptography;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace NexCode.Service.Auth;

[SupportedOSPlatform("windows")]
public sealed class MsalTokenCacheStore(
    IOptions<AuthOptions> authOptions,
    ILogger<MsalTokenCacheStore> logger)
{
    private readonly string _cacheFilePath = BuildCacheFilePath(authOptions.Value.TokenCacheFileName);
    private readonly object _sync = new();

    public string CacheFilePath => _cacheFilePath;

    public bool Exists => File.Exists(_cacheFilePath);

    public void Register(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccess(arguments =>
        {
            lock (_sync)
            {
                if (!File.Exists(_cacheFilePath))
                {
                    return;
                }

                try
                {
                    var protectedBytes = File.ReadAllBytes(_cacheFilePath);
                    var cacheBytes = ProtectedData.Unprotect(
                        protectedBytes,
                        optionalEntropy: null,
                        scope: DataProtectionScope.CurrentUser);
                    arguments.TokenCache.DeserializeMsalV3(cacheBytes);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load MSAL token cache from {CacheFilePath}", _cacheFilePath);
                }
            }
        });

        tokenCache.SetAfterAccess(arguments =>
        {
            if (!arguments.HasStateChanged)
            {
                return;
            }

            lock (_sync)
            {
                try
                {
                    var directory = Path.GetDirectoryName(_cacheFilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var cacheBytes = arguments.TokenCache.SerializeMsalV3();
                    var protectedBytes = ProtectedData.Protect(
                        cacheBytes,
                        optionalEntropy: null,
                        scope: DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(_cacheFilePath, protectedBytes);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to persist MSAL token cache to {CacheFilePath}", _cacheFilePath);
                }
            }
        });
    }

    private static string BuildCacheFilePath(string fileName)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexCode",
            "Auth");

        return Path.Combine(root, fileName);
    }
}
