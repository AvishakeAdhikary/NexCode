using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.SubAgents;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>Spec §19 <c>kill_subagent</c>. Forces a sub-agent process to exit.</summary>
public sealed class KillSubAgentTool : ITool
{
    private readonly SubAgentManager _manager;

    public KillSubAgentTool(SubAgentManager manager)
    {
        _manager = manager;
    }

    public string Name => "kill_subagent";

    public string Description => "Terminate a previously spawned sub-agent by id.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            subagent_id = new { type = "string", description = "GUID of the sub-agent to kill." }
        },
        required = new[] { "subagent_id" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<KillArgs>(context.ArgumentsJson, JsonSerialization.Options)
                   ?? throw new InvalidOperationException("Missing arguments for kill_subagent.");

        if (!Guid.TryParse(args.SubAgentId, out var id))
        {
            return Error(context, "invalid_id", $"subagent_id '{args.SubAgentId}' is not a valid GUID.");
        }

        try
        {
            var response = await _manager.KillAsync(id, cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(response, JsonSerialization.Options);
            return new ToolOutcome(Name, context.CallId, payload, IsError: false);
        }
        catch (Exception ex)
        {
            return Error(context, "kill_failed", ex.Message);
        }
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("kill_subagent", context.CallId, payload, IsError: true);
    }

    private sealed record KillArgs(
        [property: JsonPropertyName("subagent_id")] string SubAgentId);
}
