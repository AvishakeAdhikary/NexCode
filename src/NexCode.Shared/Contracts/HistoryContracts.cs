using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Spec §25 history panel DTOs. Snake_case wire format.</summary>
public sealed record HistoryListResponse(
    [property: JsonPropertyName("sessions")] HistorySummary[] Sessions);

public sealed record HistorySummary(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("project_path")] string ProjectPath,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("provider_key")] string ProviderKey,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("message_count")] int MessageCount);

public sealed record HistoryListRequest(
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("project_filter")] string? ProjectFilter);

public sealed record HistorySearchRequest(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("project_filter")] string? ProjectFilter,
    [property: JsonPropertyName("date_from_iso")] string? DateFromIso,
    [property: JsonPropertyName("date_to_iso")] string? DateToIso,
    [property: JsonPropertyName("mode_filter")] string? ModeFilter,
    [property: JsonPropertyName("provider_filter")] string? ProviderFilter,
    [property: JsonPropertyName("limit")] int Limit);

public sealed record HistorySearchResponse(
    [property: JsonPropertyName("results")] HistorySearchHit[] Results);

public sealed record HistorySearchHit(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("message_id")] Guid MessageId,
    [property: JsonPropertyName("snippet")] string Snippet,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("score")] double Score);

public sealed record HistoryArchiveRequest(
    [property: JsonPropertyName("session_id")] Guid SessionId);

public sealed record HistoryDeleteRequest(
    [property: JsonPropertyName("session_id")] Guid SessionId);

public sealed record HistoryExportRequest(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("format")] string Format);

public sealed record HistoryExportResponse(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("content")] string Content);

public sealed record SessionTitleUpdatedEventPayload(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("title")] string Title);
