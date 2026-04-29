using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Tools.Implementations;

/// <summary>Spec §10 <c>uncheck_todo_item</c>: returns an item to <see cref="TodoItemStatus.Pending"/>.</summary>
public sealed class UncheckTodoItemTool(TodoManager todoManager) : ITool
{
    public string Name => "uncheck_todo_item";
    public string Description => "Return a todo item to pending state.";
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
            ?? throw new InvalidOperationException("Missing arguments for uncheck_todo_item.");

        var ok = await todoManager.SetItemStatusAsync(args.ItemId, TodoItemStatus.Pending, cancellationToken);
        var payload = JsonSerializer.Serialize(new { item_id = args.ItemId, ok }, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: !ok);
    }

    private sealed record TodoItemArgs([property: JsonPropertyName("item_id")] Guid ItemId);
}
