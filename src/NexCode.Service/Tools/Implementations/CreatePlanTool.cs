using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §10 <c>create_plan</c>. Creates an implementation plan for the active session.
/// When <c>auto_present</c> is true (the default) the plan is created in
/// <c>pending_confirmation</c> so the WinUI shell renders the confirmation UI immediately.
/// </summary>
public sealed class CreatePlanTool(PlanManager planManager) : ITool
{
    public string Name => "create_plan";

    public string Description =>
        "Create a new implementation plan for the active session. Use this before any code-changing tools in plan mode.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "Short plan title shown to the user." },
            content = new { type = "string", description = "Markdown body of the plan." },
            auto_present = new { type = "boolean", description = "Present the plan to the user for confirmation immediately.", @default = true }
        },
        required = new[] { "title", "content" }
    });

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<CreatePlanArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for create_plan.");

        if (string.IsNullOrWhiteSpace(args.Title) || string.IsNullOrWhiteSpace(args.Content))
        {
            return Error(context, "missing_arguments", "'title' and 'content' are required.");
        }

        var planId = await planManager.CreateAsync(
            sessionId: context.Session.SessionId,
            title: args.Title,
            content: args.Content,
            autoPresent: args.AutoPresent ?? true,
            cancellationToken: cancellationToken);

        var status = (args.AutoPresent ?? true) ? "pending_confirmation" : "draft";
        var payload = JsonSerializer.Serialize(
            new CreatePlanResult(planId, status),
            JsonSerialization.Options);

        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("create_plan", context.CallId, payload, IsError: true);
    }

    private sealed record CreatePlanArguments(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("auto_present")] bool? AutoPresent);

    private sealed record CreatePlanResult(
        [property: JsonPropertyName("plan_id")] Guid PlanId,
        [property: JsonPropertyName("status")] string Status);
}
