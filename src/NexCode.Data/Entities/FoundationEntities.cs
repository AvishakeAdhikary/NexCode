using NexCode.Shared.Models;

namespace NexCode.Data.Entities;

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? MsalTokenCacheReference { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Free;
    public bool IsSuperUser { get; set; }
    public bool TelemetryEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectEntity
{
    public Guid Id { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ActiveSessionIdsJson { get; set; }
    public string? EnvironmentConfigJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SessionEntity
{
    public Guid Id { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ModeId { get; set; }
    public Guid? PersonalityId { get; set; }
    public PermissionLevel PermissionLevel { get; set; } = PermissionLevel.Default;
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Local;
    public bool SandboxEnabled { get; set; }
    public string? Title { get; set; }
    public Guid? CurrentPlanId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid? CheckpointId { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CheckpointEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? MessageId { get; set; }
    public string? GitCommitHash { get; set; }
    public string? DiffSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ModeEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? AccentColor { get; set; }
    public bool IsBuiltIn { get; set; }
    public string? AllowedToolsJson { get; set; }
}

public sealed class PersonalityEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPromptFragment { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public string Verbosity { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string Scope { get; set; } = "global";
    public Guid? ProjectId { get; set; }
}

public sealed class MemoryEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public MemoryScope Scope { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? TagsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;
}
