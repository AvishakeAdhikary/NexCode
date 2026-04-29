using System.Text.Json;
using NexCode.Service;
using NexCode.Service.Permissions;
using NexCode.Service.Tools;
using NexCode.Service.Tools.Implementations;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Plans.Tests;

public sealed class ClarifyQuestionToolTests
{
    private static ToolInvocationContext BuildContext(Guid sessionId, string argumentsJson)
    {
        var request = new SessionCreateRequest(
            ProjectPath: "C:\\proj",
            Mode: SessionMode.Plan,
            ExecutionMode: ExecutionMode.Local,
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: false);

        var session = new SessionRuntimeState(sessionId, request, DateTimeOffset.UtcNow);
        return new ToolInvocationContext(
            Session: session,
            ProjectRoot: "C:\\proj",
            SandboxEnabled: false,
            PermissionMode: PermissionMode.Default,
            CallId: "call-1",
            ArgumentsJson: argumentsJson);
    }

    private static string Args(int questionCount, int optionsPerQuestion)
    {
        var questions = new object[questionCount];
        for (var q = 0; q < questionCount; q++)
        {
            var options = new object[optionsPerQuestion];
            for (var o = 0; o < optionsPerQuestion; o++)
            {
                options[o] = new { id = $"o{o}", label = $"Option {o}" };
            }

            questions[q] = new
            {
                id = $"q{q}",
                prompt = $"Question {q}",
                type = "single_choice",
                options,
                allow_custom = false,
                required = true
            };
        }

        return JsonSerializer.Serialize(new { context = "ctx", questions }, JsonSerialization.Options);
    }

    [Fact]
    public async Task TooManyQuestions_RejectsWithStructuredError()
    {
        using var fixture = new PlansTestFixture();
        var tool = new ClarifyQuestionTool(fixture.ClarifyEngine);
        var sessionId = Guid.NewGuid();
        fixture.ClarifyEngine.BeginTurn(sessionId);

        var ctx = BuildContext(sessionId, Args(questionCount: 6, optionsPerQuestion: 2));
        var outcome = await tool.ExecuteAsync(ctx, default);

        Assert.True(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("too_many_questions", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TooManyOptionsPerQuestion_Rejected()
    {
        using var fixture = new PlansTestFixture();
        var tool = new ClarifyQuestionTool(fixture.ClarifyEngine);
        var sessionId = Guid.NewGuid();
        fixture.ClarifyEngine.BeginTurn(sessionId);

        var ctx = BuildContext(sessionId, Args(questionCount: 1, optionsPerQuestion: 5));
        var outcome = await tool.ExecuteAsync(ctx, default);

        Assert.True(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("too_many_options", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SecondCallInTurn_RejectedByAntiSpamGuard()
    {
        using var fixture = new PlansTestFixture();
        var tool = new ClarifyQuestionTool(fixture.ClarifyEngine);
        var sessionId = Guid.NewGuid();
        fixture.ClarifyEngine.BeginTurn(sessionId);

        // First call: do not await; let it park waiting for an answer. Push a respond after.
        var ctx1 = BuildContext(sessionId, Args(questionCount: 1, optionsPerQuestion: 2));
        var first = tool.ExecuteAsync(ctx1, default);

        var ctx2 = BuildContext(sessionId, Args(questionCount: 1, optionsPerQuestion: 2));
        var secondOutcome = await tool.ExecuteAsync(ctx2, default);

        Assert.True(secondOutcome.IsError);
        using var doc = JsonDocument.Parse(secondOutcome.ResultJson);
        Assert.Equal("clarify_question_already_called_this_turn", doc.RootElement.GetProperty("error").GetString());

        // Drain the first call so the test doesn't leak a 60s wait.
        await Task.Delay(50);
        await using (var db = fixture.CreateContext())
        {
            var pendingId = db.ClarifyQuestions.Single().Id;
            await fixture.ClarifyEngine.RespondAsync(new ClarifyRespondRequest(
                QuestionId: pendingId,
                Answers: Array.Empty<ClarifyAnswerItem>(),
                Cancelled: true));
        }
        await first;
    }

    [Fact]
    public async Task HappyPath_ReturnsAnswers_AsStructuredJson()
    {
        using var fixture = new PlansTestFixture();
        var tool = new ClarifyQuestionTool(fixture.ClarifyEngine);
        var sessionId = Guid.NewGuid();
        fixture.ClarifyEngine.BeginTurn(sessionId);

        var ctx = BuildContext(sessionId, Args(questionCount: 1, optionsPerQuestion: 2));
        var executeTask = tool.ExecuteAsync(ctx, default);

        // Allow the tool to register the question in the engine before responding.
        await Task.Delay(50);

        await using (var db = fixture.CreateContext())
        {
            var questionId = db.ClarifyQuestions.Single().Id;
            await fixture.ClarifyEngine.RespondAsync(new ClarifyRespondRequest(
                QuestionId: questionId,
                Answers: new[]
                {
                    new ClarifyAnswerItem(
                        QuestionId: "q0",
                        SelectedOptionIds: new[] { "o1" },
                        CustomText: null)
                },
                Cancelled: false));
        }

        var outcome = await executeTask;
        Assert.False(outcome.IsError);

        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.False(doc.RootElement.GetProperty("cancelled").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("timed_out").GetBoolean());

        var answers = doc.RootElement.GetProperty("answers");
        Assert.Equal(1, answers.GetArrayLength());
        Assert.Equal("q0", answers[0].GetProperty("question_id").GetString());
        Assert.Equal("o1", answers[0].GetProperty("selected_option_ids")[0].GetString());
    }
}
