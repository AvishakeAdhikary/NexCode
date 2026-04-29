using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §10 <c>update_plan</c>. Mutates one of: title, content, or status of an existing plan.
/// Status transitions go through <see cref="PlanManager"/>'s Appendix C state machine and may
/// fail with <c>invalid_transition</c>.
/// </summary>
public sealed class UpdatePlanTool(PlanManager planManager) : ITool
{
    public string Name => "update_plan";

    public string Description =>
        "Update an existing implementation plan (title, body, or status). Status changes are validated against the plan state machine.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            plan_id = new { type = "string", description = "Identifier of the plan to update." },
            title = new { type = "string" },
            content = new { type = "string" },
            status = new
            {
                type = "string",
                @enum = new[] { "draft", "pendingConfirmation", "confirmed", "rejected", "completed", "deleted" }
            }
        },
        required = new[] { "plan_id" }
    });

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<UpdatePlanArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for update_plan.");

        if (args.PlanId == Guid.Empty)
        {
            return Error(context, "missing_plan_id", "'plan_id' is required.");
        }

        PlanStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(args.Status))
        {
            if (!Enum.TryParse<PlanStatus>(args.Status, ignoreCase: true, out var s))
            {
                return Error(context, "invalid_status", $"Unknown plan status '{args.Status}'.");
            }
            parsedStatus = s;
        }

        try
        {
            await planManager.UpdateAsync(args.PlanId, args.Title, args.Content, parsedStatus, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Error(context, "invalid_transition", ex.Message);
        }

        var payload = JsonSerializer.Serialize(new { plan_id = args.PlanId, updated = true }, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("update_plan", context.CallId, payload, IsError: true);
    }

    private sealed record UpdatePlanArguments(
        [property: JsonPropertyName("plan_id")] Guid PlanId,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("status")] string? Status);
}
