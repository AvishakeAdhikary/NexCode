using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Spec §30 telemetry DTOs.</summary>
public sealed record TelemetryConsentRequest(
    [property: JsonPropertyName("enabled")] bool Enabled);

public sealed record TelemetryConsentResponse(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("queued_events")] int QueuedEvents);

public sealed record TelemetryQueueResponse(
    [property: JsonPropertyName("queued_events")] int QueuedEvents,
    [property: JsonPropertyName("queued_bytes")] long QueuedBytes);

public sealed record TelemetryClearResponse(
    [property: JsonPropertyName("cleared")] int Cleared);

/// <summary>One queued telemetry event prior to transmission.</summary>
public sealed record TelemetryEvent(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

public sealed record TelemetryQueueChangedEventPayload(
    [property: JsonPropertyName("queued_events")] int QueuedEvents,
    [property: JsonPropertyName("queued_bytes")] long QueuedBytes);
