using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Automations;

/// <summary>
/// Spec §22.2 trigger discriminated union. The on-disk representation is an object with a
/// <c>kind</c> string and a flat shape; we round-trip through <see cref="ParseTrigger"/> /
/// <see cref="SerializeTrigger"/> so the caller can store JSON without owning the polymorphism.
/// </summary>
public abstract record AutomationTrigger;

public sealed record ScheduleTrigger(
    [property: JsonPropertyName("cron")] string Cron) : AutomationTrigger
{
    [JsonPropertyName("kind")]
    public string Kind => "schedule";
}

public sealed record FileChangeTrigger(
    [property: JsonPropertyName("project_path")] string ProjectPath,
    [property: JsonPropertyName("glob")] string Glob,
    [property: JsonPropertyName("debounce_ms")] int DebounceMs) : AutomationTrigger
{
    [JsonPropertyName("kind")]
    public string Kind => "file_change";
}

public sealed record GitEventTrigger(
    [property: JsonPropertyName("event_type")] string EventType) : AutomationTrigger
{
    [JsonPropertyName("kind")]
    public string Kind => "git_event";
}

public sealed record SessionEndTrigger : AutomationTrigger
{
    [JsonPropertyName("kind")]
    public string Kind => "session_end";
}

public sealed record WebhookTrigger(
    [property: JsonPropertyName("path")] string Path) : AutomationTrigger
{
    [JsonPropertyName("kind")]
    public string Kind => "webhook";
}

public sealed record ManualTrigger : AutomationTrigger
{
    [JsonPropertyName("kind")]
    public string Kind => "manual";
}

/// <summary>Base for the discriminated step type. The wire format mirrors triggers.</summary>
public abstract record AutomationStep;

public sealed record RunSessionStep(
    [property: JsonPropertyName("project_path")] string ProjectPath,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("mode")] string? Mode) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "run_session";
}

public sealed record RunCommandStep(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("shell")] string? Shell,
    [property: JsonPropertyName("working_directory")] string? WorkingDirectory,
    [property: JsonPropertyName("timeout_seconds")] int? TimeoutSeconds) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "run_command";
}

public sealed record SendNotificationStep(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "send_notification";
}

public sealed record CallWebhookStep(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("headers_json")] string? HeadersJson,
    [property: JsonPropertyName("body")] string? Body) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "call_webhook";
}

public sealed record GitActionStep(
    [property: JsonPropertyName("project_path")] string ProjectPath,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("ref")] string? Ref) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "git_action";
}

public sealed record WaitStep(
    [property: JsonPropertyName("duration_ms")] int DurationMs) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "wait";
}

public sealed record ConditionalStep(
    [property: JsonPropertyName("expression")] string Expression,
    [property: JsonPropertyName("then_steps")] AutomationStep[] ThenSteps,
    [property: JsonPropertyName("else_steps")] AutomationStep[] ElseSteps) : AutomationStep
{
    [JsonPropertyName("kind")]
    public string Kind => "conditional";
}

public static class AutomationModel
{
    public static AutomationTrigger ParseTrigger(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ManualTrigger();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var kind = root.TryGetProperty("kind", out var kindEl)
            ? kindEl.GetString() ?? "manual"
            : "manual";

        return kind switch
        {
            "schedule" => Deserialize<ScheduleTrigger>(root) ?? new ManualTrigger(),
            "file_change" => Deserialize<FileChangeTrigger>(root) ?? new ManualTrigger(),
            "git_event" => Deserialize<GitEventTrigger>(root) ?? new ManualTrigger(),
            "session_end" => new SessionEndTrigger(),
            "webhook" => Deserialize<WebhookTrigger>(root) ?? new ManualTrigger(),
            _ => new ManualTrigger()
        };
    }

    public static string SerializeTrigger(AutomationTrigger trigger) =>
        JsonSerializer.Serialize<object>(trigger, JsonSerialization.Options);

    public static AutomationStep[] ParseSteps(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<AutomationStep>();
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AutomationStep>();
        }

        var list = new List<AutomationStep>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var step = ParseStep(item);
            if (step is not null)
            {
                list.Add(step);
            }
        }

        return list.ToArray();
    }

    public static AutomationStep? ParseStep(JsonElement element)
    {
        var kind = element.TryGetProperty("kind", out var kindEl)
            ? kindEl.GetString()
            : null;
        return kind switch
        {
            "run_session" => Deserialize<RunSessionStep>(element),
            "run_command" => Deserialize<RunCommandStep>(element),
            "send_notification" => Deserialize<SendNotificationStep>(element),
            "call_webhook" => Deserialize<CallWebhookStep>(element),
            "git_action" => Deserialize<GitActionStep>(element),
            "wait" => Deserialize<WaitStep>(element),
            "conditional" => Deserialize<ConditionalStep>(element),
            _ => null
        };
    }

    public static string SerializeSteps(IEnumerable<AutomationStep> steps) =>
        JsonSerializer.Serialize<object>(steps.Cast<object>().ToArray(), JsonSerialization.Options);

    private static T? Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(JsonSerialization.Options);
}
