using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Automations;

/// <summary>
/// Spec §22.2 automation engine. Owns the active-schedule registry and exposes the IPC-facing
/// CRUD surface. <see cref="AutomationScheduler"/> consumes the registry to drive cron, file,
/// git, and webhook triggers; <see cref="AutomationStepExecutor"/> executes the body.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutomationEngine(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    AutomationStepExecutor stepExecutor,
    ServiceEventHub serviceEventHub,
    ILogger<AutomationEngine> logger)
{
    private readonly ConcurrentDictionary<Guid, ScheduledAutomation> _scheduled = new();
    private readonly ConcurrentDictionary<Guid, AutomationRunStatus> _runStatus = new();

    public IReadOnlyCollection<ScheduledAutomation> ActiveSchedules => _scheduled.Values.ToArray();

    public async Task<AutomationListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Automations.AsNoTracking().ToListAsync(cancellationToken);
        var summaries = rows.Select(MapSummary).ToArray();
        return new AutomationListResponse(summaries);
    }

    public async Task<AutomationSummary> UpsertAsync(
        AutomationUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        AutomationEntity entity;
        if (request.Id is Guid existingId)
        {
            entity = await db.Automations.FirstOrDefaultAsync(a => a.Id == existingId, cancellationToken)
                     ?? new AutomationEntity { Id = existingId };
            if (db.Entry(entity).State == EntityState.Detached)
            {
                db.Automations.Add(entity);
            }
        }
        else
        {
            entity = new AutomationEntity { Id = Guid.NewGuid() };
            db.Automations.Add(entity);
        }

        entity.Name = request.Name;
        entity.TriggerJson = request.TriggerJson;
        entity.StepsJson = request.StepsJson;
        entity.Enabled = request.Enabled;

        await db.SaveChangesAsync(cancellationToken);
        RefreshSchedule(entity);
        return MapSummary(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Automations.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.Automations.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        _scheduled.TryRemove(id, out _);
        _runStatus.TryRemove(id, out _);
        return true;
    }

    public async Task<bool> ToggleAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Automations.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Enabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        RefreshSchedule(entity);
        return true;
    }

    public async Task<bool> RunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        await ExecuteAsync(entity, "manual", cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ExecuteAsync(
        AutomationEntity entity,
        string triggerKind,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        serviceEventHub.Publish(
            ServiceEventTypes.AutomationFired,
            new AutomationEventPayload(
                entity.Id,
                entity.Name,
                "running",
                triggerKind,
                startedAt,
                null,
                null));

        AutomationStep[] steps;
        try
        {
            steps = AutomationModel.ParseSteps(entity.StepsJson);
        }
        catch (Exception ex)
        {
            CompleteRun(entity, triggerKind, startedAt, success: false, error: ex.Message);
            return;
        }

        try
        {
            foreach (var step in steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await stepExecutor.ExecuteAsync(step, cancellationToken).ConfigureAwait(false);
            }

            CompleteRun(entity, triggerKind, startedAt, success: true, error: null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Automation {AutomationId} failed during execution.", entity.Id);
            CompleteRun(entity, triggerKind, startedAt, success: false, error: ex.Message);
        }
    }

    public async Task<IReadOnlyList<AutomationEntity>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Automations.AsNoTracking().ToListAsync(cancellationToken);
    }

    public void RegisterScheduled(AutomationEntity entity)
    {
        RefreshSchedule(entity);
    }

    private void CompleteRun(
        AutomationEntity entity,
        string triggerKind,
        DateTimeOffset startedAt,
        bool success,
        string? error)
    {
        var finishedAt = DateTimeOffset.UtcNow;
        _runStatus[entity.Id] = new AutomationRunStatus(finishedAt, success ? "success" : "failed");
        serviceEventHub.Publish(
            ServiceEventTypes.AutomationCompleted,
            new AutomationEventPayload(
                entity.Id,
                entity.Name,
                success ? "success" : "failed",
                triggerKind,
                startedAt,
                finishedAt,
                error));
    }

    private void RefreshSchedule(AutomationEntity entity)
    {
        if (!entity.Enabled)
        {
            _scheduled.TryRemove(entity.Id, out _);
            return;
        }

        AutomationTrigger trigger;
        try
        {
            trigger = AutomationModel.ParseTrigger(entity.TriggerJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Automation {AutomationId} has invalid trigger JSON.", entity.Id);
            _scheduled.TryRemove(entity.Id, out _);
            return;
        }

        _scheduled[entity.Id] = new ScheduledAutomation(entity, trigger);
    }

    private AutomationSummary MapSummary(AutomationEntity entity)
    {
        _runStatus.TryGetValue(entity.Id, out var status);
        return new AutomationSummary(
            Id: entity.Id,
            Name: entity.Name,
            TriggerJson: entity.TriggerJson,
            StepsJson: entity.StepsJson,
            Enabled: entity.Enabled,
            LastRunAt: status?.LastRunAt,
            LastRunStatus: status?.LastRunStatus);
    }
}

public sealed record ScheduledAutomation(AutomationEntity Entity, AutomationTrigger Trigger);

public sealed record AutomationRunStatus(DateTimeOffset LastRunAt, string LastRunStatus);
