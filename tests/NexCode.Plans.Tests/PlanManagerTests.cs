using NexCode.Service.Plans;
using NexCode.Shared.Models;

namespace NexCode.Plans.Tests;

public sealed class PlanManagerTests
{
    [Fact]
    public async Task CreateAsync_WithAutoPresent_PutsPlanIntoPendingConfirmation()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan A", "body", autoPresent: true, default);

        var detail = await fixture.PlanManager.GetAsync(planId);
        Assert.NotNull(detail);
        Assert.Equal(PlanStatus.PendingConfirmation, detail!.Status);
        Assert.Equal(1, detail.Version);
    }

    [Fact]
    public async Task CreateAsync_WithoutAutoPresent_StaysDraft()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan B", "body", autoPresent: false, default);

        var detail = await fixture.PlanManager.GetAsync(planId);
        Assert.Equal(PlanStatus.Draft, detail!.Status);
    }

    [Fact]
    public async Task FullStateMachine_DraftToCompleted_Succeeds()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: false, default);
        await fixture.PlanManager.UpdateAsync(planId, null, null, PlanStatus.PendingConfirmation, default);

        var ok = await fixture.PlanManager.ConfirmAsync(planId);
        Assert.True(ok);

        await fixture.PlanManager.UpdateAsync(planId, null, null, PlanStatus.Completed, default);

        var detail = await fixture.PlanManager.GetAsync(planId);
        Assert.Equal(PlanStatus.Completed, detail!.Status);
    }

    [Fact]
    public async Task RejectFromPending_PutsPlanIntoRejected()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: true, default);

        var ok = await fixture.PlanManager.RejectAsync(planId, "out of scope");
        Assert.True(ok);

        var detail = await fixture.PlanManager.GetAsync(planId);
        Assert.Equal(PlanStatus.Rejected, detail!.Status);
    }

    [Fact]
    public async Task ConfirmingDraftPlan_Throws_InvalidTransition()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: false, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PlanManager.ConfirmAsync(planId));
    }

    [Fact]
    public async Task RejectedPlan_CannotBeConfirmed()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: true, default);
        await fixture.PlanManager.RejectAsync(planId, "no");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PlanManager.ConfirmAsync(planId));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesPlan()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: true, default);
        await fixture.PlanManager.DeleteAsync(planId);

        var detail = await fixture.PlanManager.GetAsync(planId);
        Assert.Equal(PlanStatus.Deleted, detail!.Status);

        var list = await fixture.PlanManager.ListForSessionAsync(sessionId);
        Assert.Empty(list.Plans);
    }

    [Fact]
    public async Task PlanGate_RefusesTodoCreation_UntilPlanConfirmed()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();

        await Assert.ThrowsAsync<PlanNotConfirmedException>(() =>
            fixture.TodoManager.CreateListAsync(sessionId, null, "Tasks", null));

        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: true, default);
        await fixture.PlanManager.ConfirmAsync(planId);

        var listId = await fixture.TodoManager.CreateListAsync(sessionId, planId, "Tasks", new[] { "first" });
        Assert.NotEqual(Guid.Empty, listId);
    }

    [Fact]
    public async Task UpdateAsync_BumpsVersion_OnContentChange()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body v1", autoPresent: false, default);

        await fixture.PlanManager.UpdateAsync(planId, null, "body v2", null, default);

        var detail = await fixture.PlanManager.GetAsync(planId);
        Assert.Equal(2, detail!.Version);
    }

    [Fact]
    public async Task RequestChangesAsync_AppendsMessage()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        var planId = await fixture.PlanManager.CreateAsync(sessionId, "Plan", "body", autoPresent: true, default);

        var ok = await fixture.PlanManager.RequestChangesAsync(planId, "tighten step 3");
        Assert.True(ok);

        await using var db = fixture.CreateContext();
        var msg = db.Messages.SingleOrDefault(m => m.SessionId == sessionId);
        Assert.NotNull(msg);
        Assert.Contains("tighten step 3", msg!.Content);
    }
}
