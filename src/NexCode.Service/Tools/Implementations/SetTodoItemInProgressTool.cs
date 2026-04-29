using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Tools.Implementations;

/// <summary>Spec §10 <c>set_todo_item_in_progress</c>: marks an item as <see cref="TodoItemStatus.InProgress"/>.</summary>
public sealed class SetTodoItemInProgressTool(TodoManager todoManager) : ITool
{
    public string Name => "set_todo_item_in_progress";
    public string Description => "Mark a todo item as in_progress.";
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
            ?? throw new InvalidOperationException("Missing arguments for set_todo_item_in_progress.");

        var ok = await todoManager.SetItemStatusAsync(args.ItemId, TodoItemStatus.InProgress, cancellationToken);
        var payload = JsonSerializer.Serialize(new { item_id = args.ItemId, ok }, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: !ok);
    }

    private sealed record TodoItemArgs([property: JsonPropertyName("item_id")] Guid ItemId);
}
