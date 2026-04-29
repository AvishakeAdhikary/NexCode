using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Plans;

/// <summary>
/// Spec §11 + Appendix D Clarify engine. Owns the lifecycle of a single
/// <c>clarify_question</c> tool call: persists the question + options, publishes the
/// IPC event, and parks a <see cref="TaskCompletionSource{TResult}"/> until the user
/// responds via <see cref="RespondAsync"/> or the wait window times out.
///
/// Anti-spam (Appendix D): the agent loop calls <see cref="BeginTurn"/> /
/// <see cref="EndTurn"/> around each turn; <see cref="TryRegisterCall"/> rejects a
/// second clarify_question call inside the same turn with the
/// <c>clarify_question_already_called_this_turn</c> code.
/// </summary>
public sealed class ClarifyEngine(
    IDbContextFactory<NexCodeDbContext> contextFactory,
    ServiceEventHub eventHub)
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ClarifyResponse>> _pending = new();
    private readonly ConcurrentDictionary<Guid, int> _turnCallCounts = new();

    public void BeginTurn(Guid sessionId)
    {
        _turnCallCounts[sessionId] = 0;
    }

    public void EndTurn(Guid sessionId)
    {
        _turnCallCounts.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Attempts to register a clarify_question call for the given session in the current
    /// turn. Returns false (with code <c>clarify_question_already_called_this_turn</c>)
    /// if a clarify call has already been registered for the active turn.
    /// </summary>
    public bool TryRegisterCall(Guid sessionId)
    {
        var next = _turnCallCounts.AddOrUpdate(sessionId, 1, (_, count) => count + 1);
        return next <= 1;
    }

    public async Task<bool> EmitAsync(
        Guid sessionId,
        ClarifyQuestionEventPayload payload,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var primaryPrompt = payload.Questions.Length > 0 ? payload.Questions[0].Prompt : payload.Context;
        var question = new ClarifyQuestionEntity
        {
            Id = payload.QuestionId,
            SessionId = sessionId,
            PromptText = primaryPrompt,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ClarifyQuestions.Add(question);

        foreach (var item in payload.Questions)
        {
            for (var i = 0; i < item.Options.Length; i++)
            {
                var option = item.Options[i];
                db.ClarifyOptions.Add(new ClarifyOptionEntity
                {
                    Id = Guid.NewGuid(),
                    QuestionId = payload.QuestionId,
                    Label = $"{item.Id}::{option.Id}::{option.Label}",
                    IsCustom = false,
                    OrderIndex = i
                });
            }

            if (item.AllowCustom)
            {
                db.ClarifyOptions.Add(new ClarifyOptionEntity
                {
                    Id = Guid.NewGuid(),
                    QuestionId = payload.QuestionId,
                    Label = $"{item.Id}::__custom__::Custom answer",
                    IsCustom = true,
                    OrderIndex = item.Options.Length
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        _pending[payload.QuestionId] = new TaskCompletionSource<ClarifyResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        eventHub.Publish(ServiceEventTypes.ClarifyQuestion, payload);
        return true;
    }

    public async Task RespondAsync(
        ClarifyRespondRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var question = await db.ClarifyQuestions
            .FirstOrDefaultAsync(q => q.Id == request.QuestionId, cancellationToken);
        if (question is null)
        {
            return;
        }

        if (!request.Cancelled)
        {
            foreach (var answer in request.Answers)
            {
                db.ClarifyAnswers.Add(new ClarifyAnswerEntity
                {
                    Id = Guid.NewGuid(),
                    QuestionId = request.QuestionId,
                    SelectedOptionIdsJson = JsonSerializer.Serialize(
                        answer.SelectedOptionIds,
                        JsonSerialization.Options),
                    CustomText = answer.CustomText
                });
            }
        }

        question.Status = request.Cancelled ? "cancelled" : "answered";
        await db.SaveChangesAsync(cancellationToken);

        if (_pending.TryRemove(request.QuestionId, out var tcs))
        {
            tcs.TrySetResult(new ClarifyResponse(request.Answers, request.Cancelled, false));
        }
    }

    public async Task<ClarifyResponse> WaitForAnswerAsync(
        Guid questionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!_pending.TryGetValue(questionId, out var tcs))
        {
            tcs = new TaskCompletionSource<ClarifyResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[questionId] = tcs;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await using var registration = timeoutCts.Token.Register(static state =>
            {
                ((TaskCompletionSource<ClarifyResponse>)state!).TrySetCanceled();
            }, tcs);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(questionId, out _);

            // Spec §35 — "Timed out — AI used assumptions" is published so the shell
            // can mark the clarify card as auto-resolved.
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var question = await db.ClarifyQuestions
                .FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
            if (question is not null)
            {
                question.Status = "timed_out";
                await db.SaveChangesAsync(cancellationToken);

                eventHub.Publish(
                    ServiceEventTypes.ClarifyQuestion,
                    new ClarifyQuestionEventPayload(
                        SessionId: question.SessionId,
                        QuestionId: questionId,
                        Context: "Timed out — AI used assumptions",
                        Questions: Array.Empty<ClarifyQuestionItem>()));
            }

            return new ClarifyResponse(Array.Empty<ClarifyAnswerItem>(), Cancelled: false, TimedOut: true);
        }
    }
}

public sealed record ClarifyResponse(
    ClarifyAnswerItem[] Answers,
    bool Cancelled,
    bool TimedOut);
