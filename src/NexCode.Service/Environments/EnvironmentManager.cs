using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Service.Providers;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Environments;

/// <summary>
/// Spec §31 environment configuration manager. Each project may own one or more named
/// environments (dev, staging, prod, ...) whose secret values are encrypted column-side
/// via DPAPI on top of SQLCipher. Also loads <c>.env</c> files from the project root.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvironmentManager(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    ProviderApiKeyProtector secretProtector,
    ILogger<EnvironmentManager> logger)
{
    public async Task<EnvironmentListResponse> ListAsync(
        Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<EnvironmentConfigEntity> q = db.EnvironmentConfigs.AsNoTracking();
        if (projectId is not null)
        {
            q = q.Where(e => e.ProjectId == projectId.Value);
        }

        var rows = await q.ToListAsync(cancellationToken).ConfigureAwait(false);
        var summaries = rows.Select(MapSummary).ToArray();
        return new EnvironmentListResponse(summaries);
    }

    public async Task<EnvironmentUpsertResponse> UpsertAsync(
        EnvironmentUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Environment name is required.", nameof(request));
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        EnvironmentConfigEntity entity;
        if (request.Id is Guid id)
        {
            entity = await db.EnvironmentConfigs.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? new EnvironmentConfigEntity { Id = id };
            if (db.Entry(entity).State == EntityState.Detached)
            {
                db.EnvironmentConfigs.Add(entity);
            }
        }
        else
        {
            entity = new EnvironmentConfigEntity { Id = Guid.NewGuid() };
            db.EnvironmentConfigs.Add(entity);
        }

        entity.ProjectId = request.ProjectId;
        entity.Name = request.Name;
        entity.OverridesJson = request.OverridesJson;
        entity.EncryptedEnvVarsJson = SerializeEncrypted(request.Variables ?? Array.Empty<EnvironmentVariablePair>());

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new EnvironmentUpsertResponse(entity.Id, entity.ProjectId, entity.Name);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.EnvironmentConfigs.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null) { return false; }
        db.EnvironmentConfigs.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Resolves the merged set of environment variables for a session: starts with any
    /// stored env config rows for the project, then layers <c>.env</c> contents from disk.
    /// Returned values are decrypted in-process; never persist them outside the helper.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveForSessionAsync(
        Guid projectId,
        string projectRootPath,
        CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var rows = await db.EnvironmentConfigs
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                foreach (var pair in DeserializeEncrypted(row.EncryptedEnvVarsJson))
                {
                    merged[pair.Key] = pair.Value;
                }
            }
        }

        if (Directory.Exists(projectRootPath))
        {
            var dotenvPath = Path.Combine(projectRootPath, ".env");
            if (File.Exists(dotenvPath))
            {
                foreach (var (key, value) in ParseDotenv(dotenvPath))
                {
                    merged[key] = value;
                }
            }
        }

        return merged;
    }

    private string SerializeEncrypted(IReadOnlyList<EnvironmentVariablePair> pairs)
    {
        var protectedPairs = pairs.Select(p => new
        {
            key = p.Key,
            value = secretProtector.Protect(p.Value)
        }).ToArray();
        return JsonSerializer.Serialize(protectedPairs, JsonSerialization.Options);
    }

    private IEnumerable<KeyValuePair<string, string>> DeserializeEncrypted(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            yield break;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "EnvironmentConfig payload was not valid JSON.");
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("key", out var keyEl)
                    || !item.TryGetProperty("value", out var valueEl)
                    || keyEl.ValueKind != JsonValueKind.String
                    || valueEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                yield return new KeyValuePair<string, string>(
                    keyEl.GetString()!,
                    secretProtector.Unprotect(valueEl.GetString()!));
            }
        }
    }

    public static IEnumerable<KeyValuePair<string, string>> ParseDotenv(string filePath)
    {
        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) { continue; }

            var separator = line.IndexOf('=');
            if (separator <= 0) { continue; }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            yield return new KeyValuePair<string, string>(key, value);
        }
    }

    private EnvironmentSummary MapSummary(EnvironmentConfigEntity entity)
    {
        var keys = DeserializeEncrypted(entity.EncryptedEnvVarsJson)
            .Select(p => p.Key)
            .ToArray();
        return new EnvironmentSummary(
            Id: entity.Id,
            ProjectId: entity.ProjectId,
            Name: entity.Name,
            VariableKeys: keys,
            OverridesJson: entity.OverridesJson);
    }
}
