using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Service.Plans;

/// <summary>
/// Spec §10 + Appendix C TODO list owner. Enforces the plan-confirmation gate
/// (TODO lists may not be created until the session has at least one confirmed
/// implementation plan) and emits <see cref="ServiceEventTypes.TodoUpdated"/> on every change.
/// </summary>
public sealed class TodoManager(
    IDbContextFactory<NexCodeDbContext> contextFactory,
    ServiceEventHub eventHub,
    PlanManager planManager)
{
    public async Task<Guid> CreateListAsync(
        Guid sessionId,
        Guid? planId,
        string title,
        IReadOnlyList<string>? initialItems,
        CancellationToken cancellationToken = default)
    {
        if (!await planManager.HasConfirmedPlanAsync(sessionId, cancellationToken))
        {
            throw new PlanNotConfirmedException(sessionId);
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var list = new TodoListEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            PlanId = planId ?? Guid.Empty,
            Title = title ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TodoLists.Add(list);

        if (initialItems is { Count: > 0 })
        {
            for (var index = 0; index < initialItems.Count; index++)
            {
                var text = initialItems[index];
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                db.TodoItems.Add(new TodoItemEntity
                {
                    Id = Guid.NewGuid(),
                    ListId = list.Id,
                    Text = text,
                    Status = TodoItemStatus.Pending,
                    OrderIndex = index,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await PublishListUpdatedAsync(db, list, "list_created", cancellationToken);
        return list.Id;
    }

    public async Task<Guid> AddItemAsync(
        Guid listId,
        string text,
        Guid? insertAfterId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var list = await db.TodoLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken)
            ?? throw new InvalidOperationException($"Todo list '{listId}' was not found.");

        var items = await db.TodoItems
            .Where(i => i.ListId == listId)
            .OrderBy(i => i.OrderIndex)
            .ToListAsync(cancellationToken);

        var insertIndex = items.Count;
        if (insertAfterId is { } afterId)
        {
            var anchorIdx = items.FindIndex(i => i.Id == afterId);
            if (anchorIdx >= 0)
            {
                insertIndex = anchorIdx + 1;
            }
        }

        for (var i = insertIndex; i < items.Count; i++)
        {
            items[i].OrderIndex = i + 1;
        }

        var newItem = new TodoItemEntity
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Text = text ?? string.Empty,
            Status = TodoItemStatus.Pending,
            OrderIndex = insertIndex,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TodoItems.Add(newItem);

        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await PublishListUpdatedAsync(db, list, "item_added", cancellationToken);
        return newItem.Id;
    }

    public async Task<bool> SetItemStatusAsync(
        Guid itemId,
        TodoItemStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var item = await db.TodoItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        item.Status = newStatus;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        var list = await db.TodoLists.FirstOrDefaultAsync(l => l.Id == item.ListId, cancellationToken);
        if (list is not null)
        {
            list.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (list is not null)
        {
            await PublishListUpdatedAsync(db, list, $"item_{newStatus.ToString().ToLowerInvariant()}", cancellationToken);
        }

        return true;
    }

    public async Task<bool> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var item = await db.TodoItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var listId = item.ListId;
        db.TodoItems.Remove(item);

        var remaining = await db.TodoItems
            .Where(i => i.ListId == listId && i.Id != itemId)
            .OrderBy(i => i.OrderIndex)
            .ToListAsync(cancellationToken);
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].OrderIndex = i;
        }

        var list = await db.TodoLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken);
        if (list is not null)
        {
            list.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (list is not null)
        {
            await PublishListUpdatedAsync(db, list, "item_deleted", cancellationToken);
        }

        return true;
    }

    public async Task<bool> DeleteListAsync(Guid listId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var list = await db.TodoLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken);
        if (list is null)
        {
            return false;
        }

        var items = await db.TodoItems
            .Where(i => i.ListId == listId)
            .ToListAsync(cancellationToken);
        db.TodoItems.RemoveRange(items);
        db.TodoLists.Remove(list);

        await db.SaveChangesAsync(cancellationToken);

        eventHub.Publish(
            ServiceEventTypes.TodoUpdated,
            new TodoUpdatedEventPayload(
                SessionId: list.SessionId,
                ListId: list.Id,
                PlanId: list.PlanId == Guid.Empty ? null : list.PlanId,
                Title: list.Title,
                ChangeKind: "list_deleted",
                Items: Array.Empty<TodoItemSummary>()));

        return true;
    }

    public async Task<bool> ReorderAsync(
        Guid listId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var list = await db.TodoLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken);
        if (list is null)
        {
            return false;
        }

        var items = await db.TodoItems
            .Where(i => i.ListId == listId)
            .ToListAsync(cancellationToken);

        var lookup = items.ToDictionary(i => i.Id);
        for (var index = 0; index < orderedItemIds.Count; index++)
        {
            if (lookup.TryGetValue(orderedItemIds[index], out var entity))
            {
                entity.OrderIndex = index;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        list.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await PublishListUpdatedAsync(db, list, "reordered", cancellationToken);
        return true;
    }

    public async Task<TodoListSummary[]> ListForSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var lists = await db.TodoLists
            .AsNoTracking()
            .Where(l => l.SessionId == sessionId)
            .OrderBy(l => l.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var listIds = lists.Select(l => l.Id).ToArray();
        var items = await db.TodoItems
            .AsNoTracking()
            .Where(i => listIds.Contains(i.ListId))
            .OrderBy(i => i.OrderIndex)
            .ToArrayAsync(cancellationToken);

        return lists.Select(l => new TodoListSummary(
            ListId: l.Id,
            SessionId: l.SessionId,
            PlanId: l.PlanId == Guid.Empty ? null : l.PlanId,
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
    }

    private async Task PublishListUpdatedAsync(
        NexCodeDbContext db,
        TodoListEntity list,
        string changeKind,
        CancellationToken cancellationToken)
    {
        var items = await db.TodoItems
            .AsNoTracking()
            .Where(i => i.ListId == list.Id)
            .OrderBy(i => i.OrderIndex)
            .Select(i => new TodoItemSummary(
                i.Id,
                i.ListId,
                i.Text,
                i.Status,
                i.OrderIndex,
                i.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        eventHub.Publish(
            ServiceEventTypes.TodoUpdated,
            new TodoUpdatedEventPayload(
                SessionId: list.SessionId,
                ListId: list.Id,
                PlanId: list.PlanId == Guid.Empty ? null : list.PlanId,
                Title: list.Title,
                ChangeKind: changeKind,
                Items: items));
    }
}

/// <summary>
/// Thrown when a tool tries to create a TODO list before the session has at least one
/// confirmed implementation plan, per spec §36 + Appendix C.
/// </summary>
public sealed class PlanNotConfirmedException(Guid sessionId)
    : InvalidOperationException($"Session '{sessionId}' has no confirmed plan; create_todo_list is gated until a plan is confirmed.")
{
    public Guid SessionId { get; } = sessionId;
    public string Code => "plan_not_confirmed";
}
