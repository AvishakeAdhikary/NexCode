using NexCode.Shared.Contracts;

namespace NexCode.Plans.Tests;

public sealed class ClarifyEngineTests
{
    private static ClarifyQuestionEventPayload BuildPayload(Guid sessionId, Guid questionId)
    {
        return new ClarifyQuestionEventPayload(
            SessionId: sessionId,
            QuestionId: questionId,
            Context: "context",
            Questions: new[]
            {
                new ClarifyQuestionItem(
                    Id: "q1",
                    Prompt: "Choose one",
                    Type: "single_choice",
                    Options: new[] { new ClarifyOptionItem("a", "A"), new ClarifyOptionItem("b", "B") },
                    AllowCustom: false,
                    Required: true)
            });
    }

    [Fact]
    public void TryRegisterCall_FirstCallSucceeds_SecondCallRejected()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        fixture.ClarifyEngine.BeginTurn(sessionId);

        Assert.True(fixture.ClarifyEngine.TryRegisterCall(sessionId));
        Assert.False(fixture.ClarifyEngine.TryRegisterCall(sessionId));
    }

    [Fact]
    public void EndTurn_ResetsCallCounter()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        fixture.ClarifyEngine.BeginTurn(sessionId);
        Assert.True(fixture.ClarifyEngine.TryRegisterCall(sessionId));
        Assert.False(fixture.ClarifyEngine.TryRegisterCall(sessionId));

        fixture.ClarifyEngine.EndTurn(sessionId);
        fixture.ClarifyEngine.BeginTurn(sessionId);

        Assert.True(fixture.ClarifyEngine.TryRegisterCall(sessionId));
    }

    [Fact]
    public async Task EmitThenRespond_CompletesWaitForAnswer()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        await fixture.ClarifyEngine.EmitAsync(sessionId, BuildPayload(sessionId, questionId));

        var waiter = fixture.ClarifyEngine.WaitForAnswerAsync(questionId, TimeSpan.FromSeconds(10), default);

        await fixture.ClarifyEngine.RespondAsync(new ClarifyRespondRequest(
            QuestionId: questionId,
            Answers: new[]
            {
                new ClarifyAnswerItem(QuestionId: "q1", SelectedOptionIds: new[] { "a" }, CustomText: null)
            },
            Cancelled: false));

        var response = await waiter;

        Assert.False(response.Cancelled);
        Assert.False(response.TimedOut);
        Assert.Single(response.Answers);
        Assert.Equal("a", response.Answers[0].SelectedOptionIds[0]);
    }

    [Fact]
    public async Task WaitForAnswer_TimesOut_PublishesCancellationEvent()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        await fixture.ClarifyEngine.EmitAsync(sessionId, BuildPayload(sessionId, questionId));

        // Use a very short window; the spec uses 60s in production but we want a fast test here.
        var response = await fixture.ClarifyEngine.WaitForAnswerAsync(questionId, TimeSpan.FromMilliseconds(150), default);

        Assert.True(response.TimedOut);
        Assert.False(response.Cancelled);
        Assert.Empty(response.Answers);

        var poll = fixture.EventHub.Poll(0);
        Assert.Contains(poll.Events, e => e.EventType == ServiceEventTypes.ClarifyQuestion);
    }

    [Fact]
    public async Task RespondWithCancelled_ReturnsCancelledResponse()
    {
        using var fixture = new PlansTestFixture();
        var sessionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();

        await fixture.ClarifyEngine.EmitAsync(sessionId, BuildPayload(sessionId, questionId));

        var waiter = fixture.ClarifyEngine.WaitForAnswerAsync(questionId, TimeSpan.FromSeconds(10), default);
        await fixture.ClarifyEngine.RespondAsync(new ClarifyRespondRequest(
            QuestionId: questionId,
            Answers: Array.Empty<ClarifyAnswerItem>(),
            Cancelled: true));

        var response = await waiter;
        Assert.True(response.Cancelled);
        Assert.False(response.TimedOut);
    }
}
