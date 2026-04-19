namespace NexCode.Data.Entities;

public sealed class PluginEntity
{
    public Guid Id { get; set; }
    public string ManifestJson { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string InstallPath { get; set; } = string.Empty;
    public bool SandboxEnabled { get; set; }
}

public sealed class AutomationEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TriggerJson { get; set; } = string.Empty;
    public string StepsJson { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class McpServerEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ConnectionConfigJson { get; set; } = string.Empty;
    public bool AutoConnect { get; set; }
}

public sealed class McpServerDefinitionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
}

public sealed class ProviderEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string? ModelConfigsJson { get; set; }
}

public sealed class ThemeEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public string ColorsJson { get; set; } = "{}";
    public string FontSettingsJson { get; set; } = "{}";
}

public sealed class KeyBindingEntity
{
    public Guid Id { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string? PrimaryKeyCombo { get; set; }
    public string? SecondaryKeyCombo { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsSystem { get; set; } = true;
}

public sealed class TelemetryQueueEntity
{
    public Guid Id { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SubAgentEntity
{
    public Guid Id { get; set; }
    public Guid ParentSessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool SandboxEnabled { get; set; }
    public DateTimeOffset SpawnedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class SshKeyEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public byte[] PrivateKeyBlob { get; set; } = Array.Empty<byte>();
    public string? AssociatedGitHostsJson { get; set; }
}

public sealed class AuditLogEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public string User { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class EnvironmentConfigEntity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EncryptedEnvVarsJson { get; set; } = "{}";
    public string? OverridesJson { get; set; }
}

public sealed class EditorStateEntity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string OpenFilesJson { get; set; } = "[]";
    public string CursorStateJson { get; set; } = "{}";
    public string ScrollStateJson { get; set; } = "{}";
}

public sealed class LinterRuleEntity
{
    public Guid Id { get; set; }
    public string LanguageId { get; set; } = string.Empty;
    public string RuleSetJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public string? SeverityOverridesJson { get; set; }
}

public sealed class IapCacheEntity
{
    public Guid Id { get; set; }
    public string ProductIdsJson { get; set; } = "[]";
    public string ReceiptJson { get; set; } = "{}";
    public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Source { get; set; } = "unknown";
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
