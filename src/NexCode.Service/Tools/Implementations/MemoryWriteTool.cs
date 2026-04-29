using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Memory;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 / §9 <c>memory_write</c>: writes a key/value pair into the requested scope.
/// Default permission requirement; the GUI can later raise this to <c>Warned</c> if a
/// user opts into audited memory writes.
/// </summary>
public sealed class MemoryWriteTool(MemoryEngine memoryEngine) : ITool
{
    public string Name => "memory_write";

    public string Description =>
        "Persist a key/value pair to global, project, or session-scoped memory for later recall.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            key = new { type = "string", description = "Stable identifier for this memory." },
            value = new { type = "string", description = "Value to remember." },
            scope = new
            {
                type = "string",
                @enum = new[] { "global", "project", "session" },
                @default = "project"
            },
            tags = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional tag list for later filtering."
            }
        },
        required = new[] { "key", "value" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<MemoryWriteArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for memory_write.");

        if (string.IsNullOrWhiteSpace(arguments.Key))
        {
            return Error(context, "missing_key", "The 'key' argument is required.");
        }

        if (arguments.Value is null)
        {
            return Error(context, "missing_value", "The 'value' argument is required.");
        }

        if (!TryParseScope(arguments.Scope, out var scope))
        {
            return Error(context, "invalid_scope", $"Unknown memory scope '{arguments.Scope}'.");
        }

        var summary = await memoryEngine.WriteAsync(
            key: arguments.Key,
            value: arguments.Value,
            scope: scope,
            projectId: null,
            sessionId: scope == MemoryScope.Session ? context.Session.SessionId : null,
            tags: arguments.Tags,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new MemoryWriteResult(summary.Id, summary.Key, summary.Scope.ToString().ToLowerInvariant()),
            JsonSerialization.Options);

        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private static bool TryParseScope(string? raw, out MemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            scope = MemoryScope.Project;
            return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out scope);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("memory_write", context.CallId, payload, IsError: true);
    }

    private sealed record MemoryWriteArguments(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("tags")] string[]? Tags);

    private sealed record MemoryWriteResult(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("scope")] string Scope);
}
