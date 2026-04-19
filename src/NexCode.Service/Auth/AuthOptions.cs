namespace NexCode.Service.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string? ClientId { get; set; }

    public string? TenantId { get; set; }

    public string RedirectUri { get; set; } = "http://localhost";

    public string[] Scopes { get; set; } = ["User.Read"];

    public string TokenCacheFileName { get; set; } = "msal-token-cache.bin";
}
