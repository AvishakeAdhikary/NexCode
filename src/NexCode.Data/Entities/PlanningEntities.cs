using NexCode.Shared.Models;

namespace NexCode.Data.Entities;

public sealed class ImplementationPlanEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public int Version { get; set; } = 1;
    public string CreatedBy { get; set; } = "ai";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TodoListEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid PlanId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TodoItemEntity
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public string Text { get; set; } = string.Empty;
    public TodoItemStatus Status { get; set; } = TodoItemStatus.Pending;
    public int OrderIndex { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClarifyQuestionEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? MessageId { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClarifyOptionEntity
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsCustom { get; set; }
    public int OrderIndex { get; set; }
}

public sealed class ClarifyAnswerEntity
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public string SelectedOptionIdsJson { get; set; } = "[]";
    public string? CustomText { get; set; }
}
