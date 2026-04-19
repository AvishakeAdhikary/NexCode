using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace NexCode.Service.Auth;

[SupportedOSPlatform("windows")]
public sealed class MsalAuthService(
    IOptions<AuthOptions> authOptions,
    MsalTokenCacheStore tokenCacheStore,
    ILogger<MsalAuthService> logger) : IMsalAuthService
{
    public async Task<MsalAuthSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            return new MsalAuthSnapshot(
                IsConfigured: false,
                IsAuthenticated: false,
                HasCachedToken: tokenCacheStore.Exists,
                RequiresAuthentication: false,
                UserEmail: null,
                TokenCacheReference: tokenCacheStore.Exists ? tokenCacheStore.CacheFilePath : null,
                LastAuthenticatedAt: null);
        }

        var app = BuildClient();
        tokenCacheStore.Register(app.UserTokenCache);

        var account = (await app.GetAccountsAsync()).FirstOrDefault();
        if (account is null)
        {
            return new MsalAuthSnapshot(
                IsConfigured: true,
                IsAuthenticated: false,
                HasCachedToken: tokenCacheStore.Exists,
                RequiresAuthentication: true,
                UserEmail: null,
                TokenCacheReference: tokenCacheStore.Exists ? tokenCacheStore.CacheFilePath : null,
                LastAuthenticatedAt: null);
        }

        try
        {
            var result = await app
                .AcquireTokenSilent(GetScopes(), account)
                .ExecuteAsync(cancellationToken);

            return new MsalAuthSnapshot(
                IsConfigured: true,
                IsAuthenticated: true,
                HasCachedToken: true,
                RequiresAuthentication: false,
                UserEmail: result.Account?.Username ?? account.Username,
                TokenCacheReference: tokenCacheStore.CacheFilePath,
                LastAuthenticatedAt: DateTimeOffset.UtcNow);
        }
        catch (MsalUiRequiredException)
        {
            return new MsalAuthSnapshot(
                IsConfigured: true,
                IsAuthenticated: false,
                HasCachedToken: true,
                RequiresAuthentication: true,
                UserEmail: account.Username,
                TokenCacheReference: tokenCacheStore.CacheFilePath,
                LastAuthenticatedAt: null);
        }
    }

    public async Task<MsalAuthSnapshot> SignInInteractiveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException(
                "MSAL sign-in is not configured. Set Auth:ClientId and Auth:TenantId before attempting interactive authentication.");
        }

        var app = BuildClient();
        tokenCacheStore.Register(app.UserTokenCache);

        var result = await app
            .AcquireTokenInteractive(GetScopes())
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken);

        logger.LogInformation("MSAL interactive sign-in completed for {Username}", result.Account?.Username);

        return new MsalAuthSnapshot(
            IsConfigured: true,
            IsAuthenticated: true,
            HasCachedToken: true,
            RequiresAuthentication: false,
            UserEmail: result.Account?.Username,
            TokenCacheReference: tokenCacheStore.CacheFilePath,
            LastAuthenticatedAt: DateTimeOffset.UtcNow);
    }

    private IPublicClientApplication BuildClient()
    {
        return PublicClientApplicationBuilder
            .Create(authOptions.Value.ClientId!)
            .WithAuthority(AzureCloudInstance.AzurePublic, authOptions.Value.TenantId)
            .WithRedirectUri(authOptions.Value.RedirectUri)
            .Build();
    }

    private bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(authOptions.Value.ClientId) &&
               !string.IsNullOrWhiteSpace(authOptions.Value.TenantId);
    }

    private string[] GetScopes()
    {
        return authOptions.Value.Scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
