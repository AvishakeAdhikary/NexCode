using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Plans;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>Spec §10 <c>add_todo_item</c>. Optionally inserts after a sibling item.</summary>
public sealed class AddTodoItemTool(TodoManager todoManager) : ITool
{
    public string Name => "add_todo_item";

    public string Description => "Append (or insert) a new todo item into a list.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            list_id = new { type = "string" },
            text = new { type = "string" },
            insert_after_id = new { type = "string" }
        },
        required = new[] { "list_id", "text" }
    });

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<AddTodoItemArguments>(context.ArgumentsJson, JsonSerialization.Options)
            ?? throw new InvalidOperationException("Missing arguments for add_todo_item.");

        if (args.ListId == Guid.Empty || string.IsNullOrWhiteSpace(args.Text))
        {
            return Error(context, "missing_arguments", "'list_id' and 'text' are required.");
        }

        var itemId = await todoManager.AddItemAsync(args.ListId, args.Text, args.InsertAfterId, cancellationToken);
        var payload = JsonSerializer.Serialize(new { item_id = itemId, list_id = args.ListId }, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("add_todo_item", context.CallId, payload, IsError: true);
    }

    private sealed record AddTodoItemArguments(
        [property: JsonPropertyName("list_id")] Guid ListId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("insert_after_id")] Guid? InsertAfterId);
}
