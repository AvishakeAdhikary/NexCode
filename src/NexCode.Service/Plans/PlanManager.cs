using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Service.Plans;

/// <summary>
/// Spec §10 + Appendix C state-machine owner for ImplementationPlans. Persists plans through
/// <c>NexCodeDbContext</c>, enforces the spec's allowed status transitions, and emits
/// <see cref="ServiceEventTypes.PlanUpdated"/> envelopes for the WinUI shell + CLI.
/// </summary>
public sealed class PlanManager(
    IDbContextFactory<NexCodeDbContext> contextFactory,
    ServiceEventHub eventHub)
{
    public async Task<Guid> CreateAsync(
        Guid sessionId,
        string title,
        string content,
        bool autoPresent,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = new ImplementationPlanEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Title = title ?? string.Empty,
            Content = content ?? string.Empty,
            Status = autoPresent ? PlanStatus.PendingConfirmation : PlanStatus.Draft,
            Version = 1,
            CreatedBy = "ai",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.ImplementationPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        PublishPlanUpdated(plan, "created");
        return plan.Id;
    }

    public async Task UpdateAsync(
        Guid planId,
        string? title,
        string? content,
        PlanStatus? status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = await db.ImplementationPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        var changeKind = "updated";
        var contentChanged = false;

        if (title is not null && !string.Equals(plan.Title, title, StringComparison.Ordinal))
        {
            plan.Title = title;
            contentChanged = true;
        }

        if (content is not null && !string.Equals(plan.Content, content, StringComparison.Ordinal))
        {
            plan.Content = content;
            contentChanged = true;
        }

        if (status is { } newStatus && newStatus != plan.Status)
        {
            EnsureTransitionAllowed(plan.Status, newStatus);
            plan.Status = newStatus;
            changeKind = newStatus switch
            {
                PlanStatus.Confirmed => "confirmed",
                PlanStatus.Rejected => "rejected",
                PlanStatus.Completed => "completed",
                PlanStatus.PendingConfirmation => "presented",
                PlanStatus.Deleted => "deleted",
                _ => "updated"
            };
        }

        if (contentChanged)
        {
            plan.Version++;
        }

        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        PublishPlanUpdated(plan, changeKind);
    }

    public async Task DeleteAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = await db.ImplementationPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return;
        }

        plan.Status = PlanStatus.Deleted;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        PublishPlanUpdated(plan, "deleted");
    }

    public async Task<bool> ConfirmAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = await db.ImplementationPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return false;
        }

        EnsureTransitionAllowed(plan.Status, PlanStatus.Confirmed);
        plan.Status = PlanStatus.Confirmed;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        PublishPlanUpdated(plan, "confirmed");
        return true;
    }

    public async Task<bool> RejectAsync(
        Guid planId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = await db.ImplementationPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return false;
        }

        EnsureTransitionAllowed(plan.Status, PlanStatus.Rejected);
        plan.Status = PlanStatus.Rejected;
        plan.UpdatedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            db.Messages.Add(new MessageEntity
            {
                Id = Guid.NewGuid(),
                SessionId = plan.SessionId,
                Role = "user",
                Content = $"Plan rejected: {reason}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        PublishPlanUpdated(plan, "rejected");
        return true;
    }

    public async Task<bool> RequestChangesAsync(
        Guid planId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = await db.ImplementationPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return false;
        }

        db.Messages.Add(new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = plan.SessionId,
            Role = "user",
            Content = $"User requested changes: {notes}",
            CreatedAt = DateTimeOffset.UtcNow
        });

        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        PublishPlanUpdated(plan, "changes_requested");
        return true;
    }

    public async Task<PlanListResponse> ListForSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plans = await db.ImplementationPlans
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.Status != PlanStatus.Deleted)
            .OrderByDescending(p => p.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var summaries = plans
            .Select(p => new PlanSummary(
                PlanId: p.Id,
                SessionId: p.SessionId,
                Title: p.Title,
                Status: p.Status,
                Version: p.Version,
                CreatedAt: p.CreatedAt,
                UpdatedAt: p.UpdatedAt))
            .ToArray();

        return new PlanListResponse(sessionId, summaries);
    }

    public async Task<PlanDetail?> GetAsync(
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var plan = await db.ImplementationPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var lists = await db.TodoLists
            .AsNoTracking()
            .Where(l => l.PlanId == planId)
            .OrderBy(l => l.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var listIds = lists.Select(l => l.Id).ToArray();
        var items = await db.TodoItems
            .AsNoTracking()
            .Where(i => listIds.Contains(i.ListId))
            .OrderBy(i => i.OrderIndex)
            .ToArrayAsync(cancellationToken);

        var listSummaries = lists
            .Select(l => new TodoListSummary(
                ListId: l.Id,
                SessionId: l.SessionId,
                PlanId: l.PlanId,
                Title: l.Title,
                CreatedAt: l.CreatedAt,
                UpdatedAt: l.UpdatedAt,
                Items: items
                    .Where(i => i.ListId == l.Id)
                    .Select(i => new TodoItemSummary(
                        ItemId: i.Id,
                        ListId: i.ListId,
                        Text: i.Text,
                        Status: i.Status,
                        OrderIndex: i.OrderIndex,
                        UpdatedAt: i.UpdatedAt))
                    .ToArray()))
            .ToArray();

        return new PlanDetail(
            PlanId: plan.Id,
            SessionId: plan.SessionId,
            Title: plan.Title,
            Content: plan.Content,
            Status: plan.Status,
            Version: plan.Version,
            CreatedAt: plan.CreatedAt,
            UpdatedAt: plan.UpdatedAt,
            TodoLists: listSummaries);
    }

    public async Task<bool> HasConfirmedPlanAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ImplementationPlans
            .AsNoTracking()
            .AnyAsync(
                p => p.SessionId == sessionId &&
                     (p.Status == PlanStatus.Confirmed || p.Status == PlanStatus.Completed),
                cancellationToken);
    }

    private void PublishPlanUpdated(ImplementationPlanEntity plan, string changeKind)
    {
        eventHub.Publish(
            ServiceEventTypes.PlanUpdated,
            new PlanUpdatedEventPayload(
                SessionId: plan.SessionId,
                PlanId: plan.Id,
                Title: plan.Title,
                Status: plan.Status,
                Version: plan.Version,
                UpdatedAt: plan.UpdatedAt,
                ChangeKind: changeKind));
    }

    /// <summary>
    /// Spec Appendix C — allowed transitions:
    /// draft → pending_confirmation → confirmed | rejected; confirmed → completed.
    /// Any state may transition to <see cref="PlanStatus.Deleted"/> via soft-delete.
    /// </summary>
    private static void EnsureTransitionAllowed(PlanStatus current, PlanStatus next)
    {
        if (current == next)
        {
            return;
        }

        if (next == PlanStatus.Deleted)
        {
            return;
        }

        var allowed = current switch
        {
            PlanStatus.Draft => next is PlanStatus.PendingConfirmation,
            PlanStatus.PendingConfirmation => next is PlanStatus.Confirmed or PlanStatus.Rejected or PlanStatus.Draft,
            PlanStatus.Confirmed => next is PlanStatus.Completed,
            PlanStatus.Rejected => false,
            PlanStatus.Completed => false,
            PlanStatus.Deleted => false,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Plan transition '{current}' → '{next}' is not permitted by Appendix C.");
        }
    }
}
