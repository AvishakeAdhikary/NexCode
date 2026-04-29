using NexCode.Shared.Models;

namespace NexCode.Plans.Tests;

public sealed class TodoManagerTests
{
    private static async Task<(PlansTestFixture fixture, Guid sessionId, Guid planId)> SeedConfirmedPlanAsync()
    {
        var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: true, default);
        await fixture.PlanManager.ConfirmAsync(planId);
        return (fixture, sessionId, planId);
    }

    [Fact]
    public async Task CreateListAsync_PersistsInitialItems_InOrder()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "a", "b", "c" });

        var lists = await fixture.TodoManager.ListForSessionAsync(sessionId);
        var list = Assert.Single(lists);
        Assert.Equal(listId, list.ListId);
        Assert.Collection(list.Items,
            item => Assert.Equal("a", item.Text),
            item => Assert.Equal("b", item.Text),
            item => Assert.Equal("c", item.Text));
    }

    [Fact]
    public async Task AddItemAsync_AppendsAndAssignsContiguousOrderIndex()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "first" });
        await fixture.TodoManager.AddItemAsync(listId, "second", null);
        await fixture.TodoManager.AddItemAsync(listId, "third", null);

        var lists = await fixture.TodoManager.ListForSessionAsync(sessionId);
        var items = lists[0].Items;
        Assert.Equal(new[] { "first", "second", "third" }, items.Select(i => i.Text).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, items.Select(i => i.OrderIndex).ToArray());
    }

    [Fact]
    public async Task AddItemAsync_InsertAfterId_RespectsAnchor()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "a", "c" });
        var lists = await fixture.TodoManager.ListForSessionAsync(sessionId);
        var firstItemId = lists[0].Items[0].ItemId;

        await fixture.TodoManager.AddItemAsync(listId, "b", firstItemId);

        var refreshed = await fixture.TodoManager.ListForSessionAsync(sessionId);
        Assert.Equal(new[] { "a", "b", "c" }, refreshed[0].Items.Select(i => i.Text).ToArray());
    }

    [Fact]
    public async Task SetItemStatus_TransitionsThroughAllStates()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "task" });
        var itemId = (await fixture.TodoManager.ListForSessionAsync(sessionId))[0].Items[0].ItemId;

        Assert.True(await fixture.TodoManager.SetItemStatusAsync(itemId, TodoItemStatus.InProgress));
        Assert.True(await fixture.TodoManager.SetItemStatusAsync(itemId, TodoItemStatus.Done));
        Assert.True(await fixture.TodoManager.SetItemStatusAsync(itemId, TodoItemStatus.Pending));
        Assert.True(await fixture.TodoManager.SetItemStatusAsync(itemId, TodoItemStatus.Skipped));

        var item = (await fixture.TodoManager.ListForSessionAsync(sessionId))[0].Items[0];
        Assert.Equal(TodoItemStatus.Skipped, item.Status);
    }

    [Fact]
    public async Task DeleteItemAsync_ClosesGap_AndCompactsOrderIndex()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "a", "b", "c" });
        var middle = (await fixture.TodoManager.ListForSessionAsync(sessionId))[0].Items[1].ItemId;

        Assert.True(await fixture.TodoManager.DeleteItemAsync(middle));

        var refreshed = await fixture.TodoManager.ListForSessionAsync(sessionId);
        Assert.Equal(new[] { "a", "c" }, refreshed[0].Items.Select(i => i.Text).ToArray());
        Assert.Equal(new[] { 0, 1 }, refreshed[0].Items.Select(i => i.OrderIndex).ToArray());
    }

    [Fact]
    public async Task ReorderAsync_AppliesNewOrder()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "a", "b", "c" });
        var ids = (await fixture.TodoManager.ListForSessionAsync(sessionId))[0]
            .Items.Select(i => i.ItemId).ToArray();

        // Reverse the order
        var reversed = ids.Reverse().ToArray();
        Assert.True(await fixture.TodoManager.ReorderAsync(listId, reversed));

        var refreshed = await fixture.TodoManager.ListForSessionAsync(sessionId);
        Assert.Equal(new[] { "c", "b", "a" }, refreshed[0].Items.Select(i => i.Text).ToArray());
    }

    [Fact]
    public async Task DeleteListAsync_RemovesListAndItems()
    {
        var (fixture, sessionId, planId) = await SeedConfirmedPlanAsync();
        using var _ = fixture;

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "x", "y" });
        Assert.True(await fixture.TodoManager.DeleteListAsync(listId));

        var lists = await fixture.TodoManager.ListForSessionAsync(sessionId);
        Assert.Empty(lists);
    }
}
