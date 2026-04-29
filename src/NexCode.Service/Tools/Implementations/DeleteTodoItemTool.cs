using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>Spec §10 <c>delete_todo_item</c>: removes an item from a list.</summary>
public sealed class DeleteTodoItemTool(TodoManager todoManager) : ITool
{
    public string Name => "delete_todo_item";
    public string Description => "Delete a todo item from its list.";
    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { item_id = new { type = "string" } },
        required = new[] { "item_id" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<TodoItemArgs>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for delete_todo_item.");

        var ok = await todoManager.DeleteItemAsync(args.ItemId, cancellationToken);
        var payload = JsonSerializer.Serialize(new { item_id = args.ItemId, deleted = ok }, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: !ok);
    }

    private sealed record TodoItemArgs([property: JsonPropertyName("item_id")] Guid ItemId);
}
