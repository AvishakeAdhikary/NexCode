using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Auth;
using NexCode.Service.Permissions;
using NexCode.Service.SubAgents;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §19 <c>spawn_subagent</c>. Tier-gated (Pro+ via <see cref="SubscriptionCapabilityPolicy"/>).
/// Sub-agent inherits the parent session's permission level + sandbox flag — no escalation.
/// </summary>
public sealed class SpawnSubAgentTool : ITool
{
    private readonly SubAgentManager _manager;
    private readonly AccountStateService _accountState;

    public SpawnSubAgentTool(SubAgentManager manager, AccountStateService accountState)
    {
        _manager = manager;
        _accountState = accountState;
    }

    public string Name => "spawn_subagent";

    public string Description =>
        "Spawn a delegated sub-agent in an isolated worker process. Inherits the parent session's permission level and sandbox flag.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            config_json = new
            {
                type = "string",
                description = "JSON config blob describing the sub-agent's mode, prompt, and tools."
            }
        },
        required = new[] { "config_json" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<SpawnArgs>(context.ArgumentsJson, JsonSerialization.Options)
                   ?? throw new InvalidOperationException("Missing arguments for spawn_subagent.");

        if (string.IsNullOrWhiteSpace(args.ConfigJson))
        {
            return Error(context, "missing_config", "config_json is required.");
        }

        var snapshot = await _accountState.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Capabilities.MaxSubAgentsPerSession <= 0)
        {
            return Error(context, "tier_required", "Sub-agents require NexCode Pro or higher.");
        }

        try
        {
            var response = await _manager.SpawnAsync(
                new SubAgentSpawnRequest(context.Session.SessionId, args.ConfigJson),
                snapshot.Capabilities,
                new SubAgentManager.PermissionLevelDescriptor(
                    PermissionLevel: context.PermissionMode == PermissionMode.Full
                        ? PermissionLevel.Full
                        : PermissionLevel.Default,
                    SandboxEnabled: context.SandboxEnabled),
                cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(response, JsonSerialization.Options);
            return new ToolOutcome(Name, context.CallId, payload, IsError: false);
        }
        catch (Exception ex)
        {
            return Error(context, "spawn_failed", ex.Message);
        }
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("spawn_subagent", context.CallId, payload, IsError: true);
    }

    private sealed record SpawnArgs(
        [property: JsonPropertyName("config_json")] string ConfigJson);
}
