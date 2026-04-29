using System.Text.Json.Serialization;

namespace NexCode.Shared.Contracts;

/// <summary>Spec §31 environment configuration DTOs.</summary>
public sealed record EnvironmentListRequest(
    [property: JsonPropertyName("project_id")] Guid? ProjectId);

public sealed record EnvironmentListResponse(
    [property: JsonPropertyName("environments")] EnvironmentSummary[] Environments);

public sealed record EnvironmentSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("project_id")] Guid ProjectId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("variable_keys")] string[] VariableKeys,
    [property: JsonPropertyName("overrides_json")] string? OverridesJson);

public sealed record EnvironmentVariablePair(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value);

public sealed record EnvironmentUpsertRequest(
    [property: JsonPropertyName("id")] Guid? Id,
    [property: JsonPropertyName("project_id")] Guid ProjectId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("variables")] EnvironmentVariablePair[] Variables,
    [property: JsonPropertyName("overrides_json")] string? OverridesJson);

public sealed record EnvironmentUpsertResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("project_id")] Guid ProjectId,
    [property: JsonPropertyName("name")] string Name);

public sealed record EnvironmentDeleteRequest(
    [property: JsonPropertyName("id")] Guid Id);
