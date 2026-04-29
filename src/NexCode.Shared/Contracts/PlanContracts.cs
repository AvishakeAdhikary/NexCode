using System.Text.Json.Serialization;
using NexCode.Shared.Models;

namespace NexCode.Shared.Contracts;

/// <summary>
/// Spec §10 + §11 + Appendix C/D contracts shared by the WinUI shell, CLI, and helper service.
/// All wire DTOs use snake_case <see cref="JsonPropertyNameAttribute"/>s so they match the
/// public IPC schema documented in the spec.
/// </summary>
public sealed record PlanUpdatedEventPayload(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] PlanStatus Status,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("change_kind")] string ChangeKind);

public sealed record TodoUpdatedEventPayload(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("list_id")] Guid ListId,
    [property: JsonPropertyName("plan_id")] Guid? PlanId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("change_kind")] string ChangeKind,
    [property: JsonPropertyName("items")] TodoItemSummary[] Items);

public sealed record TodoListSummary(
    [property: JsonPropertyName("list_id")] Guid ListId,
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("plan_id")] Guid? PlanId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("items")] TodoItemSummary[] Items);

public sealed record TodoItemSummary(
    [property: JsonPropertyName("item_id")] Guid ItemId,
    [property: JsonPropertyName("list_id")] Guid ListId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("status")] TodoItemStatus Status,
    [property: JsonPropertyName("order_index")] int OrderIndex,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record ClarifyQuestionEventPayload(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("question_id")] Guid QuestionId,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("questions")] ClarifyQuestionItem[] Questions);

public sealed record ClarifyQuestionItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("options")] ClarifyOptionItem[] Options,
    [property: JsonPropertyName("allow_custom")] bool AllowCustom,
    [property: JsonPropertyName("required")] bool Required);

public sealed record ClarifyOptionItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label);

public sealed record ClarifyRespondRequest(
    [property: JsonPropertyName("question_id")] Guid QuestionId,
    [property: JsonPropertyName("answers")] ClarifyAnswerItem[] Answers,
    [property: JsonPropertyName("cancelled")] bool Cancelled);

public sealed record ClarifyAnswerItem(
    [property: JsonPropertyName("question_id")] string QuestionId,
    [property: JsonPropertyName("selected_option_ids")] string[] SelectedOptionIds,
    [property: JsonPropertyName("custom_text")] string? CustomText);

public sealed record PlanListResponse(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("plans")] PlanSummary[] Plans);

public sealed record PlanSummary(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] PlanStatus Status,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record PlanDetail(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("status")] PlanStatus Status,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("todo_lists")] TodoListSummary[] TodoLists);

public sealed record PlanRequestChangesRequest(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("notes")] string Notes);

public sealed record PlanListRequest(
    [property: JsonPropertyName("session_id")] Guid SessionId);

public sealed record PlanGetRequest(
    [property: JsonPropertyName("plan_id")] Guid PlanId);

public sealed record TodoListRequest(
    [property: JsonPropertyName("session_id")] Guid SessionId);

public sealed record TodoListResponse(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("lists")] TodoListSummary[] Lists);

public sealed record TodoItemMutationRequest(
    [property: JsonPropertyName("item_id")] Guid ItemId);

public sealed record TodoAddItemRequest(
    [property: JsonPropertyName("list_id")] Guid ListId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("insert_after_id")] Guid? InsertAfterId);

public sealed record TodoReorderRequest(
    [property: JsonPropertyName("list_id")] Guid ListId,
    [property: JsonPropertyName("ordered_item_ids")] Guid[] OrderedItemIds);
