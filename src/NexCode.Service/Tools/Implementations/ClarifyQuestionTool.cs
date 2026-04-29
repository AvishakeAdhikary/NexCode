using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §11.2 <c>clarify_question</c>. Validates the call (max 5 questions, max 4 options each)
/// honors the Appendix D anti-spam guard (one clarify call per turn), emits the IPC event,
/// and waits up to 60 seconds for the user response. Returns the JSON-encoded
/// <see cref="ClarifyAnswerItem"/> array (or a timed-out marker) to the model.
/// </summary>
public sealed class ClarifyQuestionTool(ClarifyEngine clarifyEngine) : ITool
{
    private const int MaxQuestions = 5;
    private const int MaxOptionsPerQuestion = 4;
    private static readonly TimeSpan WaitWindow = TimeSpan.FromSeconds(60);

    public string Name => "clarify_question";

    public string Description =>
        "Ask the user up to 5 multiple-choice clarifying questions. Use sparingly: at most one clarify_question call per turn.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            context = new { type = "string", description = "Short framing string shown above the questions." },
            questions = new
            {
                type = "array",
                maxItems = MaxQuestions,
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string" },
                        prompt = new { type = "string" },
                        type = new { type = "string", @enum = new[] { "single_choice", "multi_choice" } },
                        options = new
                        {
                            type = "array",
                            maxItems = MaxOptionsPerQuestion,
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    id = new { type = "string" },
                                    label = new { type = "string" }
                                },
                                required = new[] { "id", "label" }
                            }
                        },
                        allow_custom = new { type = "boolean", @default = false },
                        required = new { type = "boolean", @default = true }
                    },
                    required = new[] { "id", "prompt", "type", "options" }
                }
            }
        },
        required = new[] { "context", "questions" }
    });

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<ClarifyArgs>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for clarify_question.");

        if (args.Questions is null || args.Questions.Length == 0)
        {
            return Error(context, "missing_questions", "At least one question is required.");
        }

        if (args.Questions.Length > MaxQuestions)
        {
            return Error(context, "too_many_questions", $"clarify_question accepts at most {MaxQuestions} questions per call.");
        }

        foreach (var q in args.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Prompt))
            {
                return Error(context, "invalid_question", "Each question requires 'id' and 'prompt'.");
            }

            if (q.Type is not ("single_choice" or "multi_choice"))
            {
                return Error(context, "invalid_type", $"Unknown question type '{q.Type}'.");
            }

            var options = q.Options ?? Array.Empty<ClarifyOptionItem>();
            if (options.Length == 0)
            {
                return Error(context, "missing_options", $"Question '{q.Id}' has no options.");
            }

            if (options.Length > MaxOptionsPerQuestion)
            {
                return Error(context, "too_many_options", $"Question '{q.Id}' has more than {MaxOptionsPerQuestion} options.");
            }
        }

        if (!clarifyEngine.TryRegisterCall(context.Session.SessionId))
        {
            return Error(
                context,
                "clarify_question_already_called_this_turn",
                "clarify_question may only be called once per turn (Appendix D anti-spam).");
        }

        var questionId = Guid.NewGuid();
        var payload = new ClarifyQuestionEventPayload(
            SessionId: context.Session.SessionId,
            QuestionId: questionId,
            Context: args.Context ?? string.Empty,
            Questions: args.Questions);

        await clarifyEngine.EmitAsync(context.Session.SessionId, payload, cancellationToken);
        var response = await clarifyEngine.WaitForAnswerAsync(questionId, WaitWindow, cancellationToken);

        var resultPayload = JsonSerializer.Serialize(
            new ClarifyResult(
                QuestionId: questionId,
                Answers: response.Answers,
                Cancelled: response.Cancelled,
                TimedOut: response.TimedOut),
            JsonSerialization.Options);

        return new ToolOutcome(Name, context.CallId, resultPayload, IsError: false);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("clarify_question", context.CallId, payload, IsError: true);
    }

    private sealed record ClarifyArgs(
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("questions")] ClarifyQuestionItem[] Questions);

    private sealed record ClarifyResult(
        [property: JsonPropertyName("question_id")] Guid QuestionId,
        [property: JsonPropertyName("answers")] ClarifyAnswerItem[] Answers,
        [property: JsonPropertyName("cancelled")] bool Cancelled,
        [property: JsonPropertyName("timed_out")] bool TimedOut);
}
