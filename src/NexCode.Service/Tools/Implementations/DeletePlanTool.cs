using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §10 <c>delete_plan</c>. Soft-deletes a plan by marking its status as
/// <c>deleted</c>. Issues a <see cref="ServiceEventTypes.PlanUpdated"/> event so any open
/// shells can dismiss the plan card.
/// </summary>
public sealed class DeletePlanTool(PlanManager planManager) : ITool
{
    public string Name => "delete_plan";

    public string Description => "Soft-delete an implementation plan.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            plan_id = new { type = "string", description = "Identifier of the plan to delete." }
        },
        required = new[] { "plan_id" }
    });

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<DeletePlanArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for delete_plan.");

        if (args.PlanId == Guid.Empty)
        {
            var err = JsonSerializer.Serialize(
                new { error = "missing_plan_id", message = "'plan_id' is required." },
                JsonSerialization.Options);
            return new ToolOutcome(Name, context.CallId, err, IsError: true);
        }

        await planManager.DeleteAsync(args.PlanId, cancellationToken);
        var payload = JsonSerializer.Serialize(
            new { plan_id = args.PlanId, deleted = true },
            JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private sealed record DeletePlanArguments(
        [property: JsonPropertyName("plan_id")] Guid PlanId);
}
