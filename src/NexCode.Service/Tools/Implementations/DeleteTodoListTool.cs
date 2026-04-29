using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>Spec §10 <c>delete_todo_list</c>: removes a TODO list and its items.</summary>
public sealed class DeleteTodoListTool(TodoManager todoManager) : ITool
{
    public string Name => "delete_todo_list";
    public string Description => "Delete an entire todo list (and all of its items).";
    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { list_id = new { type = "string" } },
        required = new[] { "list_id" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<DeleteListArgs>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for delete_todo_list.");

        var ok = await todoManager.DeleteListAsync(args.ListId, cancellationToken);
        var payload = JsonSerializer.Serialize(new { list_id = args.ListId, deleted = ok }, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: !ok);
    }

    private sealed record DeleteListArgs([property: JsonPropertyName("list_id")] Guid ListId);
}
