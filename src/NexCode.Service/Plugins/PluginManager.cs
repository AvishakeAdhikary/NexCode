using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Plugins;

/// <summary>
/// Spec §22.1 plugin lifecycle: list, install, uninstall, toggle. Install copies the source
/// folder into the per-user plugin root, validates the manifest, optionally verifies the
/// signature, and persists a row. Uninstall removes both the row and the folder.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PluginManager(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    PluginSignatureVerifier signatureVerifier,
    ILogger<PluginManager> logger)
{
    public static string DefaultPluginRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexCode",
        "Plugins");

    private readonly string _pluginRoot = DefaultPluginRoot;

    public async Task<PluginListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Plugins.AsNoTracking().ToListAsync(cancellationToken);
        var summaries = rows
            .Select(MapSummary)
            .ToArray();
        return new PluginListResponse(summaries);
    }

    public async Task<PluginSummary> InstallAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Plugin source path '{sourcePath}' does not exist.");
        }

        var manifestPath = Path.Combine(sourcePath, PluginManifest.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Plugin manifest is missing.", manifestPath);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = PluginManifest.TryParse(manifestJson, out var error);
        if (manifest is null)
        {
            throw new InvalidDataException($"Plugin manifest invalid: {error}");
        }

        var pluginId = Guid.NewGuid();
        Directory.CreateDirectory(_pluginRoot);
        var installPath = Path.Combine(_pluginRoot, pluginId.ToString("N"));
        CopyDirectory(sourcePath, installPath);

        if (!signatureVerifier.Verify(installPath, manifest))
        {
            TryDeleteFolder(installPath);
            throw new UnauthorizedAccessException("Plugin signature verification failed.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new PluginEntity
        {
            Id = pluginId,
            ManifestJson = manifest.ToJson(),
            Enabled = true,
            InstallPath = installPath,
            SandboxEnabled = true
        };
        db.Plugins.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Installed plugin '{Name}' v{Version} at {InstallPath}.",
            manifest.Name,
            manifest.Version,
            installPath);

        return MapSummary(entity);
    }

    public async Task<bool> UninstallAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Plugins.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.Plugins.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        TryDeleteFolder(entity.InstallPath);
        return true;
    }

    public async Task<bool> ToggleAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Plugins.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Enabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<(PluginEntity Entity, PluginManifest Manifest)>> GetEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Plugins.AsNoTracking().Where(p => p.Enabled).ToListAsync(cancellationToken);
        var results = new List<(PluginEntity, PluginManifest)>(rows.Count);
        foreach (var row in rows)
        {
            var manifest = PluginManifest.TryParse(row.ManifestJson, out _);
            if (manifest is not null)
            {
                results.Add((row, manifest));
            }
        }

        return results;
    }

    private static PluginSummary MapSummary(PluginEntity entity)
    {
        var manifest = PluginManifest.TryParse(entity.ManifestJson, out _);
        return new PluginSummary(
            Id: entity.Id,
            Name: manifest?.Name ?? "(invalid manifest)",
            Version: manifest?.Version ?? string.Empty,
            ManifestJson: entity.ManifestJson,
            Enabled: entity.Enabled,
            InstallPath: entity.InstallPath,
            Sandboxed: entity.SandboxEnabled);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort cleanup; the next install attempt will overwrite the folder anyway.
        }
    }
}
