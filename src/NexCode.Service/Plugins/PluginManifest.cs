using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Plugins;

/// <summary>
/// Spec §22.1 / AD-0005 plugin manifest schema. Plugins ship a <c>nexcode-plugin.json</c>
/// file at the root of their install folder declaring their metadata, hook subscriptions,
/// and capability requirements. Signature is optional; production hardening requires it.
/// </summary>
public sealed record PluginManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("entry_point")] string EntryPoint,
    [property: JsonPropertyName("hooks")] string[] Hooks,
    [property: JsonPropertyName("permissions")] string[] Permissions,
    [property: JsonPropertyName("signature")] string? Signature)
{
    public const string ManifestFileName = "nexcode-plugin.json";

    public static readonly string[] AllowedHooks =
    [
        "on_session_start",
        "on_message_before_send",
        "on_tool_call_before",
        "on_tool_call_after",
        "on_checkpoint",
        "on_session_end",
        "on_memory_write",
        "on_plan_confirmed",
        "on_todo_completed"
    ];

    /// <summary>Parses a manifest JSON document. Returns <c>null</c> if invalid.</summary>
    public static PluginManifest? TryParse(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "manifest_empty";
            return null;
        }

        PluginManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PluginManifest>(json, JsonSerialization.Options);
        }
        catch (JsonException ex)
        {
            error = $"manifest_invalid_json:{ex.Message}";
            return null;
        }

        if (parsed is null)
        {
            error = "manifest_null";
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.Name))
        {
            error = "manifest_missing_name";
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.Version))
        {
            error = "manifest_missing_version";
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.EntryPoint))
        {
            error = "manifest_missing_entry_point";
            return null;
        }

        var hooks = parsed.Hooks ?? Array.Empty<string>();
        foreach (var hook in hooks)
        {
            if (Array.IndexOf(AllowedHooks, hook) < 0)
            {
                error = $"manifest_unknown_hook:{hook}";
                return null;
            }
        }

        return parsed with
        {
            Hooks = hooks,
            Permissions = parsed.Permissions ?? Array.Empty<string>()
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerialization.Options);
}
