namespace NexCode.Shared.Contracts;

/// <summary>Provider summary returned by <c>provider.list</c>.</summary>
public sealed record ProviderListResponse(
    ProviderSummary[] Providers,
    string? DefaultProviderKey);

public sealed record ProviderSummary(
    string ProviderKey,
    string DisplayName,
    string BaseUrl,
    bool HasApiKey,
    string DefaultModelId,
    bool IsDefault);

/// <summary>
/// Request body for <c>provider.upsert</c>. <see cref="ApiKey"/> is plaintext only on the wire
/// from the trusted GUI process to the trusted helper; the helper persists it encrypted.
/// </summary>
public sealed record ProviderUpsertRequest(
    string ProviderKey,
    string DisplayName,
    string BaseUrl,
    string ApiKey,
    string DefaultModelId,
    bool MakeDefault);

public sealed record ProviderUpsertResponse(
    string ProviderKey,
    bool IsDefault);

public sealed record ProviderRemoveRequest(string ProviderKey);

public sealed record ProviderRemoveResponse(string ProviderKey, bool Removed);

public sealed record ProviderSetDefaultRequest(string ProviderKey);

public sealed record ProviderSetDefaultResponse(string ProviderKey, bool IsDefault);
