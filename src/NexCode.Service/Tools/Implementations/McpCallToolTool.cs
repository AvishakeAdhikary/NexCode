using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Mcp;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §17 <c>mcp_call_tool</c>: dispatches a tool call against any connected MCP server.
/// Per-server permission overrides are handled inside <see cref="McpManager"/>.
/// </summary>
public sealed class McpCallToolTool : ITool
{
    private readonly McpManager _manager;

    public McpCallToolTool(McpManager manager)
    {
        _manager = manager;
    }

    public string Name => "mcp_call_tool";

    public string Description =>
        "Call a tool exposed by a connected MCP server. Server is selected by id; arguments are an opaque JSON object passed to the server.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            server_id = new { type = "string", description = "GUID of the connected MCP server." },
            tool_name = new { type = "string", description = "Tool name as published by the server." },
            arguments = new { type = "object", description = "Arguments object forwarded verbatim to the MCP server." }
        },
        required = new[] { "server_id", "tool_name" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<McpCallArguments>(context.ArgumentsJson, JsonSerialization.Options)
                   ?? throw new InvalidOperationException("Missing arguments for mcp_call_tool.");

        if (!Guid.TryParse(args.ServerId, out var serverId))
        {
            return Error(context, "invalid_server_id", $"Server id '{args.ServerId}' is not a valid GUID.");
        }
        if (string.IsNullOrWhiteSpace(args.ToolName))
        {
            return Error(context, "missing_tool_name", "tool_name is required.");
        }

        var argsJson = args.Arguments?.GetRawText() ?? "{}";

        try
        {
            var response = await _manager
                .CallToolAsync(serverId, args.ToolName, argsJson, cancellationToken)
                .ConfigureAwait(false);
            return new ToolOutcome(
                ToolName: Name,
                CallId: context.CallId,
                ResultJson: response.ResultJson,
                IsError: response.IsError);
        }
        catch (Exception ex)
        {
            return Error(context, "mcp_call_failed", ex.Message);
        }
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("mcp_call_tool", context.CallId, payload, IsError: true);
    }

    private sealed record McpCallArguments(
        [property: JsonPropertyName("server_id")] string ServerId,
        [property: JsonPropertyName("tool_name")] string ToolName,
        [property: JsonPropertyName("arguments")] JsonElement? Arguments);
}
