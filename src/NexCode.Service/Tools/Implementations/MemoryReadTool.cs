using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Memory;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 / §9 <c>memory_read</c>: looks up a remembered fact by key inside the requested
/// scope. Read-only and side-effect-light (only updates LastAccessedAt), so its
/// <see cref="PermissionRequirement"/> is <see cref="ToolPermissionRequirement.Default"/>.
/// </summary>
public sealed class MemoryReadTool(MemoryEngine memoryEngine) : ITool
{
    public string Name => "memory_read";

    public string Description =>
        "Read a remembered key/value pair from global, project, or session-scoped memory.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            key = new { type = "string", description = "The memory key to look up." },
            scope = new
            {
                type = "string",
                @enum = new[] { "global", "project", "session" },
                description = "Memory scope to read from.",
                @default = "global"
            }
        },
        required = new[] { "key" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<MemoryReadArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for memory_read.");

        if (string.IsNullOrWhiteSpace(arguments.Key))
        {
            return Error(context, "missing_key", "The 'key' argument is required.");
        }

        if (!TryParseScope(arguments.Scope, out var scope))
        {
            return Error(context, "invalid_scope", $"Unknown memory scope '{arguments.Scope}'.");
        }

        var summary = await memoryEngine.ReadAsync(
            key: arguments.Key,
            scope: scope,
            projectId: null,
            sessionId: scope == MemoryScope.Session ? context.Session.SessionId : null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new MemoryReadResult(arguments.Key, summary?.Value, summary is not null),
            JsonSerialization.Options);

        return new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false);
    }

    private static bool TryParseScope(string? raw, out MemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            scope = MemoryScope.Global;
            return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out scope);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("memory_read", context.CallId, payload, IsError: true);
    }

    private sealed record MemoryReadArguments(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record MemoryReadResult(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("found")] bool Found);
}
