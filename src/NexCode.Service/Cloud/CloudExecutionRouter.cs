using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexCode.Service.Remote;

namespace NexCode.Service.Cloud;

/// <summary>
/// Spec §23 cloud branch — when ExecutionMode is Cloud, sessions are sent over
/// the same gRPC pipeline but to NexCode's hosted endpoint (env
/// <c>NEXCODE_CLOUD_ENDPOINT</c>). This router tracks the last cloud sync
/// timestamp via a per-project side file and provides a diff-based zip sync
/// stub that the future cloud-sync slice will fill out.
/// </summary>
public sealed class CloudExecutionRouter(
    ILogger<CloudExecutionRouter> logger,
    RemoteSessionClient remoteSessionClient)
{
    public const string CloudEndpointEnv = "NEXCODE_CLOUD_ENDPOINT";
    private const string SyncMarkerFileName = ".nexcode-cloud-last-sync";

    public string? CloudEndpoint => Environment.GetEnvironmentVariable(CloudEndpointEnv);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(CloudEndpoint);

    public async Task<DateTimeOffset?> GetLastSyncAsync(string projectRoot)
    {
        var marker = Path.Combine(projectRoot, SyncMarkerFileName);
        if (!File.Exists(marker))
        {
            return null;
        }

        var raw = await File.ReadAllTextAsync(marker).ConfigureAwait(false);
        return DateTimeOffset.TryParse(raw, out var ts) ? ts : null;
    }

    public async Task<CloudSyncResult> SyncProjectAsync(
        string projectRoot,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException($"Project root does not exist: {projectRoot}");
        }

        var lastSync = await GetLastSyncAsync(projectRoot).ConfigureAwait(false);
        var changedFiles = EnumerateChangedFiles(projectRoot, lastSync).ToList();

        var zipPath = Path.Combine(Path.GetTempPath(), $"nexcode-cloud-{sessionId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.zip");
        if (changedFiles.Count > 0)
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in changedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
            }
        }

        // Future slice: stream zipPath bytes through remoteSessionClient.SendAsync.
        // For now we just record the marker so subsequent syncs are incremental.
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, SyncMarkerFileName),
            now.ToString("O"),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Cloud sync prepared {Count} files for session {SessionId} (zip: {Zip}). Endpoint: {Endpoint}.",
            changedFiles.Count,
            sessionId,
            zipPath,
            CloudEndpoint);

        return new CloudSyncResult(now, changedFiles.Count, zipPath, remoteSessionClient.IsConnected);
    }

    private static IEnumerable<string> EnumerateChangedFiles(string projectRoot, DateTimeOffset? since)
    {
        var thresholdUtc = since?.UtcDateTime ?? DateTime.MinValue;
        return Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Where(file =>
            {
                if (file.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains("/.git/", StringComparison.Ordinal) ||
                    file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(SyncMarkerFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                try
                {
                    var info = new FileInfo(file);
                    return info.LastWriteTimeUtc > thresholdUtc;
                }
                catch
                {
                    return false;
                }
            });
    }
}

public sealed record CloudSyncResult(DateTimeOffset SyncedAt, int FilesIncluded, string ArchivePath, bool RemoteConnected);
