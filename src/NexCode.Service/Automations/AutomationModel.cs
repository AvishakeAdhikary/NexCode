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
            "schedule" => (AutomationTrigger?)Deserialize<ScheduleTrigger>(root) ?? new ManualTrigger(),
            "file_change" => (AutomationTrigger?)Deserialize<FileChangeTrigger>(root) ?? new ManualTrigger(),
            "git_event" => (AutomationTrigger?)Deserialize<GitEventTrigger>(root) ?? new ManualTrigger(),
            "session_end" => new SessionEndTrigger(),
            "webhook" => (AutomationTrigger?)Deserialize<WebhookTrigger>(root) ?? new ManualTrigger(),
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
            "conditional" => ParseConditionalStep(element),
            _ => null
        };
    }

    private static ConditionalStep? ParseConditionalStep(JsonElement element)
    {
        var expression = element.TryGetProperty("expression", out var expr) && expr.ValueKind == JsonValueKind.String
            ? expr.GetString() ?? string.Empty
            : string.Empty;
        var thenSteps = ParseNestedSteps(element, "then_steps");
        var elseSteps = ParseNestedSteps(element, "else_steps");
        return new ConditionalStep(expression, thenSteps, elseSteps);
    }

    private static AutomationStep[] ParseNestedSteps(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AutomationStep>();
        }

        var list = new List<AutomationStep>();
        foreach (var item in arr.EnumerateArray())
        {
            var step = ParseStep(item);
            if (step is not null) list.Add(step);
        }
        return list.ToArray();
    }

    public static string SerializeSteps(IEnumerable<AutomationStep> steps)
    {
        // System.Text.Json uses the declared static type for array elements, which loses
        // the polymorphic "kind" discriminator on nested AutomationStep[] inside a
        // ConditionalStep. We serialize manually to JsonNode so each step is written by
        // its runtime type, then recursively re-serialize the nested then_steps/else_steps.
        var array = new System.Text.Json.Nodes.JsonArray();
        foreach (var step in steps)
        {
            array.Add(SerializeStepNode(step));
        }
        return array.ToJsonString(JsonSerialization.Options);
    }

    private static System.Text.Json.Nodes.JsonNode? SerializeStepNode(AutomationStep step)
    {
        if (step is ConditionalStep conditional)
        {
            var thenArr = new System.Text.Json.Nodes.JsonArray();
            foreach (var s in conditional.ThenSteps) thenArr.Add(SerializeStepNode(s));
            var elseArr = new System.Text.Json.Nodes.JsonArray();
            foreach (var s in conditional.ElseSteps) elseArr.Add(SerializeStepNode(s));
            return new System.Text.Json.Nodes.JsonObject
            {
                ["kind"] = "conditional",
                ["expression"] = conditional.Expression,
                ["then_steps"] = thenArr,
                ["else_steps"] = elseArr,
            };
        }

        // For non-conditional steps, use the runtime type so the kind property is emitted.
        var json = JsonSerializer.Serialize(step, step.GetType(), JsonSerialization.Options);
        return System.Text.Json.Nodes.JsonNode.Parse(json);
    }

    private static T? Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(JsonSerialization.Options);
}
