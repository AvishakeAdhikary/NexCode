using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Memory;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 / §9 <c>memory_list</c>: lists memories visible to the agent. Read-only and
/// inexpensive, so its <see cref="PermissionRequirement"/> is
/// <see cref="ToolPermissionRequirement.Default"/>.
/// </summary>
public sealed class MemoryListTool(MemoryEngine memoryEngine) : ITool
{
    public string Name => "memory_list";

    public string Description =>
        "List remembered key/value pairs in the given scope, ordered by most recently accessed.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            scope = new
            {
                type = "string",
                @enum = new[] { "global", "project", "session" },
                description = "Optional scope filter; when omitted all durable memories are returned."
            },
            project_id = new { type = "string", description = "Optional project id filter for project-scoped memories." }
        }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<MemoryListArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? new MemoryListArguments(null, null);

        MemoryScope? scope = null;
        if (!string.IsNullOrWhiteSpace(arguments.Scope))
        {
            if (!Enum.TryParse<MemoryScope>(arguments.Scope, ignoreCase: true, out var parsed))
            {
                return Error(context, "invalid_scope", $"Unknown memory scope '{arguments.Scope}'.");
            }
            scope = parsed;
        }

        Guid? projectId = null;
        if (!string.IsNullOrWhiteSpace(arguments.ProjectId)
            && Guid.TryParse(arguments.ProjectId, out var parsedId))
        {
            projectId = parsedId;
        }

        var summaries = await memoryEngine.ListAsync(
            scope: scope,
            projectId: projectId,
            sessionId: context.Session.SessionId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new MemoryListResult(summaries),
            JsonSerialization.Options);

        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("memory_list", context.CallId, payload, IsError: true);
    }

    private sealed record MemoryListArguments(
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("project_id")] string? ProjectId);

    private sealed record MemoryListResult(
        [property: JsonPropertyName("memories")] Shared.Contracts.MemorySummary[] Memories);
}
