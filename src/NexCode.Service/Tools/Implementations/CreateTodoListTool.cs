using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §10 <c>create_todo_list</c>. Refuses with code <c>plan_not_confirmed</c> when the
/// session does not yet have a confirmed plan (Appendix C plan-confirmation gate, §36).
/// </summary>
public sealed class CreateTodoListTool(TodoManager todoManager) : ITool
{
    public string Name => "create_todo_list";

    public string Description =>
        "Create a TODO list (with optional initial items) for the active session. Requires a confirmed plan.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string" },
            plan_id = new { type = "string", description = "Optional plan to attach the list to." },
            items = new
            {
                type = "array",
                items = new { type = "string" }
            }
        },
        required = new[] { "title" }
    });

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<CreateTodoListArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for create_todo_list.");

        if (string.IsNullOrWhiteSpace(args.Title))
        {
            return Error(context, "missing_title", "'title' is required.");
        }

        try
        {
            var listId = await todoManager.CreateListAsync(
                sessionId: context.Session.SessionId,
                planId: args.PlanId,
                title: args.Title,
                initialItems: args.Items,
                cancellationToken: cancellationToken);

            var payload = JsonSerializer.Serialize(
                new { list_id = listId, item_count = args.Items?.Length ?? 0 },
                JsonSerialization.Options);
            return new ToolOutcome(Name, context.CallId, payload, IsError: false);
        }
        catch (PlanNotConfirmedException ex)
        {
            return Error(context, ex.Code, ex.Message);
        }
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("create_todo_list", context.CallId, payload, IsError: true);
    }

    private sealed record CreateTodoListArguments(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("plan_id")] Guid? PlanId,
        [property: JsonPropertyName("items")] string[]? Items);
}
