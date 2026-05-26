using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Service.Auth;
using NexCode.Service.Permissions;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Service.SubAgents;

/// <summary>
/// Spec §19 / AD-0009 sub-agent supervisor. Spawns isolated <c>NexCode.Cli subagent</c>
/// child processes per sub-agent, tracks their lifecycle in the encrypted
/// <c>SubAgents</c> table, gates on subscription tier, and forwards JSON-RPC notifications
/// from the child stdout into <see cref="ServiceEventHub"/>.
/// </summary>
public sealed class SubAgentManager
{
    private readonly IDbContextFactory<NexCodeDbContext> _dbContextFactory;
    private readonly ServiceEventHub _eventHub;
    private readonly ILogger<SubAgentManager> _logger;
    private readonly ConcurrentDictionary<Guid, ChildProcess> _live = new();
    private readonly ISubAgentLauncher _launcher;

    public SubAgentManager(
        IDbContextFactory<NexCodeDbContext> dbContextFactory,
        ServiceEventHub eventHub,
        ILogger<SubAgentManager> logger,
        ISubAgentLauncher? launcher = null)
    {
        _dbContextFactory = dbContextFactory;
        _eventHub = eventHub;
        _logger = logger;
        _launcher = launcher ?? new DefaultSubAgentLauncher();
    }

    public async Task<SubAgentSpawnResponse> SpawnAsync(
        SubAgentSpawnRequest request,
        SubscriptionCapabilitiesPayload capabilities,
        PermissionLevelDescriptor permissionContext,
        CancellationToken cancellationToken)
    {
        if (capabilities.MaxSubAgentsPerSession <= 0)
        {
            throw new InvalidOperationException("Sub-agents require NexCode Pro or higher.");
        }

        var activeForSession = _live.Values.Count(p => p.ParentSessionId == request.ParentSessionId && !p.HasExited);
        if (activeForSession >= capabilities.MaxSubAgentsPerSession)
        {
            throw new InvalidOperationException(
                $"Subscription tier limits this session to {capabilities.MaxSubAgentsPerSession} concurrent sub-agents.");
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = new SubAgentEntity
        {
            Id = Guid.NewGuid(),
            ParentSessionId = request.ParentSessionId,
            Status = SubAgentStatuses.Pending,
            ConfigJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson,
            SandboxEnabled = permissionContext.SandboxEnabled,
            SpawnedAt = DateTimeOffset.UtcNow,
        };
        db.SubAgents.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var process = _launcher.Launch(entity.Id, request.ParentSessionId, entity.ConfigJson);
            var child = new ChildProcess(entity.Id, request.ParentSessionId, process);
            _live[entity.Id] = child;

            entity.Status = SubAgentStatuses.Running;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _eventHub.Publish(
                ServiceEventTypes.AgentSpawned,
                new AgentSpawnedEventPayload(entity.Id, entity.ParentSessionId, entity.ConfigJson));

            // Stream stdout JSON lines and exit notifications.
            _ = Task.Run(() => MonitorChildAsync(child), CancellationToken.None);
        }
        catch (Exception ex)
        {
            entity.Status = SubAgentStatuses.Failed;
            entity.EndedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            _eventHub.Publish(
                ServiceEventTypes.AgentEnded,
                new AgentEndedEventPayload(entity.Id, entity.ParentSessionId, SubAgentStatuses.Failed, null, ex.Message));
            throw;
        }

        return new SubAgentSpawnResponse(entity.Id);
    }

    public async Task<SubAgentKillResponse> KillAsync(Guid subAgentId, CancellationToken cancellationToken)
    {
        // Persist "killed" to the DB before signalling the process so that the background
        // MonitorChildAsync/OnChildExitAsync task sees EndedAt != null and skips its own
        // status write, preventing a race where it overwrites "killed" with "failed".
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.SubAgents.FirstOrDefaultAsync(x => x.Id == subAgentId, cancellationToken).ConfigureAwait(false);
        if (entity is not null && entity.EndedAt is null)
        {
            entity.Status = SubAgentStatuses.Killed;
            entity.EndedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _eventHub.Publish(
                ServiceEventTypes.AgentEnded,
                new AgentEndedEventPayload(entity.Id, entity.ParentSessionId, SubAgentStatuses.Killed, null, null));
        }

        var killed = false;
        if (_live.TryRemove(subAgentId, out var child))
        {
            killed = child.TryKill();
        }

        return new SubAgentKillResponse(subAgentId, killed);
    }

    public async Task<SubAgentListResponse> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.SubAgents
            .AsNoTracking()
            .OrderByDescending(x => x.SpawnedAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(r => new SubAgentSummary(
            r.Id,
            r.ParentSessionId,
            r.Status,
            r.ConfigJson,
            r.SpawnedAt,
            r.EndedAt)).ToArray();
        return new SubAgentListResponse(items);
    }

    private async Task MonitorChildAsync(ChildProcess child)
    {
        var process = child.Process;
        try
        {
            // Forward stdout JSON-Lines as events.
            using var reader = process.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                ForwardChildLine(child, line);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sub-agent {Id} monitor failed.", child.Id);
        }
        finally
        {
            await OnChildExitAsync(child).ConfigureAwait(false);
        }
    }

    private void ForwardChildLine(ChildProcess child, string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var paramsEl = root.TryGetProperty("params", out var p) ? p : default;
            if (string.IsNullOrEmpty(method))
            {
                return;
            }
            _eventHub.Publish(
                method!,
                new
                {
                    subagent_id = child.Id,
                    parent_session_id = child.ParentSessionId,
                    @params = paramsEl,
                });
        }
        catch (JsonException)
        {
            // Treat non-JSON output as a status line.
            _eventHub.Publish(
                ServiceEventTypes.Status,
                new StatusEventPayload(child.ParentSessionId, line, "info"));
        }
    }

    private async Task OnChildExitAsync(ChildProcess child)
    {
        _live.TryRemove(child.Id, out _);

        int? exitCode = null;
        try
        {
            exitCode = child.Process.ExitCode;
        }
        catch
        {
            // not exited
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            var entity = await db.SubAgents.FirstOrDefaultAsync(x => x.Id == child.Id).ConfigureAwait(false);
            if (entity is not null && entity.EndedAt is null)
            {
                entity.EndedAt = DateTimeOffset.UtcNow;
                entity.Status = exitCode == 0 ? SubAgentStatuses.Completed : SubAgentStatuses.Failed;
                await db.SaveChangesAsync().ConfigureAwait(false);

                _eventHub.Publish(
                    ServiceEventTypes.AgentEnded,
                    new AgentEndedEventPayload(
                        entity.Id,
                        entity.ParentSessionId,
                        entity.Status,
                        exitCode,
                        null));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record sub-agent {Id} exit.", child.Id);
        }
    }

    public sealed record PermissionLevelDescriptor(PermissionLevel PermissionLevel, bool SandboxEnabled);

    private sealed class ChildProcess
    {
        public ChildProcess(Guid id, Guid parentSessionId, Process process)
        {
            Id = id;
            ParentSessionId = parentSessionId;
            Process = process;
        }

        public Guid Id { get; }
        public Guid ParentSessionId { get; }
        public Process Process { get; }
        public bool HasExited
        {
            get { try { return Process.HasExited; } catch { return true; } }
        }

        public bool TryKill()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    return true;
                }
            }
            catch
            {
                // ignore
            }
            return false;
        }
    }
}

public interface ISubAgentLauncher
{
    Process Launch(Guid subAgentId, Guid parentSessionId, string configJson);
}

internal sealed class DefaultSubAgentLauncher : ISubAgentLauncher
{
    public Process Launch(Guid subAgentId, Guid parentSessionId, string configJson)
    {
        var dllPath = ResolveCliDll();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(dllPath);
        psi.ArgumentList.Add("subagent");
        psi.ArgumentList.Add("--id");
        psi.ArgumentList.Add(subAgentId.ToString());
        psi.ArgumentList.Add("--parent");
        psi.ArgumentList.Add(parentSessionId.ToString());
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(configJson ?? "{}");

        return Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to spawn sub-agent process.");
    }

    private static string ResolveCliDll()
    {
        var baseDir = AppContext.BaseDirectory;
        // Helper service ships next to the CLI binary in the deployment layout.
        var candidate = Path.Combine(baseDir, "NexCode.Cli.dll");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Fallback for development layouts: ../NexCode.Cli/<config>/<tfm>/NexCode.Cli.dll
        var probe = Path.Combine(baseDir, "..", "..", "..", "..", "NexCode.Cli", "bin");
        if (Directory.Exists(probe))
        {
            var found = Directory
                .EnumerateFiles(probe, "NexCode.Cli.dll", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }
        return candidate; // returns missing path so the caller surfaces a clean error
    }
}
