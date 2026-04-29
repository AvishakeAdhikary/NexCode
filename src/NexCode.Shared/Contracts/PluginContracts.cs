using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>
/// Spec §22.1 plugin system DTOs. Wire-format snake_case so the helper IPC schema is stable
/// across the WinUI shell, CLI, and any out-of-process plugin tooling.
/// </summary>
public sealed record PluginListResponse(
    [property: JsonPropertyName("plugins")] PluginSummary[] Plugins);

public sealed record PluginSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("manifest_json")] string ManifestJson,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("install_path")] string InstallPath,
    [property: JsonPropertyName("sandboxed")] bool Sandboxed);

public sealed record PluginInstallRequest(
    [property: JsonPropertyName("source_path")] string SourcePath);

public sealed record PluginUninstallRequest(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record PluginToggleRequest(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("enabled")] bool Enabled);

/// <summary>
/// Service event payload published on <c>plugin.event</c>. <see cref="Event"/> is the hook
/// name (e.g. <c>on_session_start</c>) and <see cref="MessageJson"/> carries any structured
/// reply the plugin produced.
/// </summary>
public sealed record PluginEventPayload(
    [property: JsonPropertyName("plugin_id")] Guid PluginId,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("message_json")] string MessageJson);
