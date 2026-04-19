namespace NexCode.Service.Auth;

public sealed record MsalAuthSnapshot(
    bool IsConfigured,
    bool IsAuthenticated,
    bool HasCachedToken,
    bool RequiresAuthentication,
    string? UserEmail,
    string? TokenCacheReference,
    DateTimeOffset? LastAuthenticatedAt);
