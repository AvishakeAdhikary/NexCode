using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Reads and persists provider configuration from the encrypted <c>Providers</c> table
/// (schema §2.3). API keys are double-protected: column-level DPAPI envelope on top of
/// page-level SQLCipher AES-256.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProviderConfigurationService(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    ProviderApiKeyProtector apiKeyProtector)
{
    public async Task<IReadOnlyList<ProviderConfiguration>> GetAllConfiguredAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Providers.AsNoTracking().ToListAsync(cancellationToken);
        var configs = new List<ProviderConfiguration>(rows.Count);

        foreach (var row in rows)
        {
            var meta = ParseMeta(row.ModelConfigsJson);
            var apiKey = apiKeyProtector.Unprotect(row.EncryptedApiKey);
            configs.Add(new ProviderConfiguration(
                ProviderKey: row.Name,
                DisplayName: meta.DisplayName ?? row.Name,
                BaseUrl: row.BaseUrl,
                ApiKey: apiKey,
                DefaultModelId: meta.DefaultModelId ?? string.Empty));
        }

        return configs;
    }

    public async Task<ProviderConfiguration?> FindConfiguredAsync(
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == providerKey, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var meta = ParseMeta(row.ModelConfigsJson);
        var apiKey = apiKeyProtector.Unprotect(row.EncryptedApiKey);
        return new ProviderConfiguration(
            ProviderKey: row.Name,
            DisplayName: meta.DisplayName ?? row.Name,
            BaseUrl: row.BaseUrl,
            ApiKey: apiKey,
            DefaultModelId: meta.DefaultModelId ?? string.Empty);
    }

    public async Task<string?> GetDefaultProviderKeyAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Providers.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var meta = ParseMeta(row.ModelConfigsJson);
            if (meta.IsDefault)
            {
                return row.Name;
            }
        }

        return rows.Count == 1 ? rows[0].Name : null;
    }

    public async Task<ProviderListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Providers.AsNoTracking().ToListAsync(cancellationToken);
        string? defaultKey = null;
        var summaries = new List<ProviderSummary>(rows.Count);

        foreach (var row in rows)
        {
            var meta = ParseMeta(row.ModelConfigsJson);
            if (meta.IsDefault)
            {
                defaultKey = row.Name;
            }

            summaries.Add(new ProviderSummary(
                ProviderKey: row.Name,
                DisplayName: meta.DisplayName ?? row.Name,
                BaseUrl: row.BaseUrl,
                HasApiKey: !string.IsNullOrEmpty(row.EncryptedApiKey),
                DefaultModelId: meta.DefaultModelId ?? string.Empty,
                IsDefault: meta.IsDefault));
        }

        if (defaultKey is null && summaries.Count == 1)
        {
            defaultKey = summaries[0].ProviderKey;
        }

        return new ProviderListResponse(summaries.ToArray(), defaultKey);
    }

    public async Task<ProviderUpsertResponse> UpsertAsync(
        ProviderUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderKey))
        {
            throw new ArgumentException("ProviderKey is required.", nameof(request));
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var encryptedKey = string.IsNullOrEmpty(request.ApiKey)
            ? string.Empty
            : apiKeyProtector.Protect(request.ApiKey);

        var existing = await db.Providers.FirstOrDefaultAsync(
            p => p.Name == request.ProviderKey,
            cancellationToken);

        var willBeDefault = request.MakeDefault;
        if (!willBeDefault)
        {
            var totalCount = await db.Providers.CountAsync(cancellationToken);
            // First provider added always becomes default for first-run convenience.
            if (totalCount == 0 && existing is null)
            {
                willBeDefault = true;
            }
        }

        if (existing is null)
        {
            existing = new ProviderEntity
            {
                Id = Guid.NewGuid(),
                Name = request.ProviderKey,
                BaseUrl = request.BaseUrl,
                EncryptedApiKey = encryptedKey,
                ModelConfigsJson = WriteMeta(new ProviderMeta(
                    DisplayName: request.DisplayName,
                    DefaultModelId: request.DefaultModelId,
                    IsDefault: willBeDefault))
            };
            db.Providers.Add(existing);
        }
        else
        {
            existing.BaseUrl = request.BaseUrl;
            if (!string.IsNullOrEmpty(request.ApiKey))
            {
                existing.EncryptedApiKey = encryptedKey;
            }
            existing.ModelConfigsJson = WriteMeta(new ProviderMeta(
                DisplayName: request.DisplayName,
                DefaultModelId: request.DefaultModelId,
                IsDefault: willBeDefault));
        }

        if (willBeDefault)
        {
            var others = await db.Providers
                .Where(p => p.Name != request.ProviderKey)
                .ToListAsync(cancellationToken);
            foreach (var other in others)
            {
                var meta = ParseMeta(other.ModelConfigsJson);
                if (!meta.IsDefault)
                {
                    continue;
                }

                other.ModelConfigsJson = WriteMeta(meta with { IsDefault = false });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ProviderUpsertResponse(request.ProviderKey, willBeDefault);
    }

    public async Task<bool> RemoveAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Providers.FirstOrDefaultAsync(
            p => p.Name == providerKey,
            cancellationToken);
        if (row is null)
        {
            return false;
        }

        db.Providers.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetDefaultAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Providers.ToListAsync(cancellationToken);
        var found = false;

        foreach (var row in rows)
        {
            var meta = ParseMeta(row.ModelConfigsJson);
            if (row.Name == providerKey)
            {
                row.ModelConfigsJson = WriteMeta(meta with { IsDefault = true });
                found = true;
            }
            else if (meta.IsDefault)
            {
                row.ModelConfigsJson = WriteMeta(meta with { IsDefault = false });
            }
        }

        if (!found)
        {
            return false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private sealed record ProviderMeta(
        string? DisplayName,
        string? DefaultModelId,
        bool IsDefault);

    private static ProviderMeta ParseMeta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProviderMeta(null, null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ProviderMeta(
                DisplayName: TryGetString(root, "display_name"),
                DefaultModelId: TryGetString(root, "default_model_id"),
                IsDefault: root.TryGetProperty("is_default", out var def)
                    && def.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return new ProviderMeta(null, null, false);
        }
    }

    private static string WriteMeta(ProviderMeta meta) =>
        JsonSerializer.Serialize(new
        {
            display_name = meta.DisplayName,
            default_model_id = meta.DefaultModelId,
            is_default = meta.IsDefault
        }, JsonSerialization.Options);

    private static string? TryGetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
