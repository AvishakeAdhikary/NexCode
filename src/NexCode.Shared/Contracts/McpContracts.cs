using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Spec §17 contracts: snake-case wire shapes for MCP server management IPC.</summary>
public sealed record McpListResponse(
    [property: JsonPropertyName("servers")] McpServerSummary[] Servers);

public sealed record McpServerSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("connection_config_json")] string ConnectionConfigJson,
    [property: JsonPropertyName("auto_connect")] bool AutoConnect,
    [property: JsonPropertyName("status")] string Status);

public sealed record McpUpsertRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("transport_type")] string TransportType,
    [property: JsonPropertyName("connection_config_json")] string ConnectionConfigJson,
    [property: JsonPropertyName("auto_connect")] bool AutoConnect,
    [property: JsonPropertyName("id")] Guid? Id = null);

public sealed record McpUpsertResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("created")] bool Created);

public sealed record McpRemoveRequest(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record McpConnectRequest(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record McpDisconnectRequest(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record McpCallToolRequest(
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("arguments_json")] string ArgumentsJson);

public sealed record McpCallToolResponse(
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("result_json")] string ResultJson,
    [property: JsonPropertyName("is_error")] bool IsError);

public sealed record McpStatusEventPayload(
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("error_message")] string? ErrorMessage);

public sealed record McpToolEventPayload(
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("result_json")] string? ResultJson);

public sealed record McpToolDescriptor(
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] JsonElement InputSchema);

public static class McpServerStatuses
{
    public const string Disconnected = "disconnected";
    public const string Connecting = "connecting";
    public const string Connected = "connected";
    public const string Error = "error";
}

public static class McpTransportTypes
{
    public const string Stdio = "stdio";
    public const string StreamableHttp = "streamable_http";
}
