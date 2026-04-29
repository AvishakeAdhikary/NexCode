using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Spec §22.2 automation engine DTOs. Snake_case wire format.</summary>
public sealed record AutomationListResponse(
    [property: JsonPropertyName("automations")] AutomationSummary[] Automations);

public sealed record AutomationSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("trigger_json")] string TriggerJson,
    [property: JsonPropertyName("steps_json")] string StepsJson,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("last_run_at")] DateTimeOffset? LastRunAt,
    [property: JsonPropertyName("last_run_status")] string? LastRunStatus);

public sealed record AutomationUpsertRequest(
    [property: JsonPropertyName("id")] Guid? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("trigger_json")] string TriggerJson,
    [property: JsonPropertyName("steps_json")] string StepsJson,
    [property: JsonPropertyName("enabled")] bool Enabled);

public sealed record AutomationRunRequest(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record AutomationDeleteRequest(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record AutomationToggleRequest(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("enabled")] bool Enabled);

/// <summary>
/// Payload of <c>automation.fired</c> / <c>automation.completed</c> service events.
/// </summary>
public sealed record AutomationEventPayload(
    [property: JsonPropertyName("automation_id")] Guid AutomationId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("trigger_kind")] string TriggerKind,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("finished_at")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("error")] string? Error);
