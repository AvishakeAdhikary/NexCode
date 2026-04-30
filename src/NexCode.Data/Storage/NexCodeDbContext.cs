using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NexCode.Data.Entities;

namespace NexCode.Data.Storage;

public sealed class NexCodeDbContext(DbContextOptions<NexCodeDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<CheckpointEntity> Checkpoints => Set<CheckpointEntity>();
    public DbSet<ModeEntity> Modes => Set<ModeEntity>();
    public DbSet<PersonalityEntity> Personalities => Set<PersonalityEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    public DbSet<ImplementationPlanEntity> ImplementationPlans => Set<ImplementationPlanEntity>();
    public DbSet<TodoListEntity> TodoLists => Set<TodoListEntity>();
    public DbSet<TodoItemEntity> TodoItems => Set<TodoItemEntity>();
    public DbSet<ClarifyQuestionEntity> ClarifyQuestions => Set<ClarifyQuestionEntity>();
    public DbSet<ClarifyOptionEntity> ClarifyOptions => Set<ClarifyOptionEntity>();
    public DbSet<ClarifyAnswerEntity> ClarifyAnswers => Set<ClarifyAnswerEntity>();
    public DbSet<PluginEntity> Plugins => Set<PluginEntity>();
    public DbSet<AutomationEntity> Automations => Set<AutomationEntity>();
    public DbSet<McpServerEntity> McpServers => Set<McpServerEntity>();
    public DbSet<McpServerDefinitionEntity> McpServerDefinitions => Set<McpServerDefinitionEntity>();
    public DbSet<ProviderEntity> Providers => Set<ProviderEntity>();
    public DbSet<ThemeEntity> Themes => Set<ThemeEntity>();
    public DbSet<KeyBindingEntity> KeyBindings => Set<KeyBindingEntity>();
    public DbSet<TelemetryQueueEntity> TelemetryQueue => Set<TelemetryQueueEntity>();
    public DbSet<SubAgentEntity> SubAgents => Set<SubAgentEntity>();
    public DbSet<SshKeyEntity> SshKeys => Set<SshKeyEntity>();
    public DbSet<AuditLogEntity> AuditLog => Set<AuditLogEntity>();
    public DbSet<EnvironmentConfigEntity> EnvironmentConfigs => Set<EnvironmentConfigEntity>();
    public DbSet<EditorStateEntity> EditorStates => Set<EditorStateEntity>();
    public DbSet<LinterRuleEntity> LinterRules => Set<LinterRuleEntity>();
    public DbSet<IapCacheEntity> IapCache => Set<IapCacheEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTable<UserEntity>(modelBuilder, "Users");
        ConfigureTable<ProjectEntity>(modelBuilder, "Projects");
        ConfigureTable<SessionEntity>(modelBuilder, "Sessions");
        ConfigureTable<MessageEntity>(modelBuilder, "Messages");
        ConfigureTable<CheckpointEntity>(modelBuilder, "Checkpoints");
        ConfigureTable<ModeEntity>(modelBuilder, "Modes");
        ConfigureTable<PersonalityEntity>(modelBuilder, "Personalities");
        ConfigureTable<MemoryEntity>(modelBuilder, "Memories");
        ConfigureTable<ImplementationPlanEntity>(modelBuilder, "ImplementationPlans");
        ConfigureTable<TodoListEntity>(modelBuilder, "TodoLists");
        ConfigureTable<TodoItemEntity>(modelBuilder, "TodoItems");
        ConfigureTable<ClarifyQuestionEntity>(modelBuilder, "ClarifyQuestions");
        ConfigureTable<ClarifyOptionEntity>(modelBuilder, "ClarifyOptions");
        ConfigureTable<ClarifyAnswerEntity>(modelBuilder, "ClarifyAnswers");
        ConfigureTable<PluginEntity>(modelBuilder, "Plugins");
        ConfigureTable<AutomationEntity>(modelBuilder, "Automations");
        ConfigureTable<McpServerEntity>(modelBuilder, "MCPServers");
        ConfigureTable<McpServerDefinitionEntity>(modelBuilder, "MCPServerDefs");
        ConfigureTable<ProviderEntity>(modelBuilder, "Providers");
        ConfigureTable<ThemeEntity>(modelBuilder, "Themes");
        ConfigureTable<KeyBindingEntity>(modelBuilder, "KeyBindings");
        ConfigureTable<TelemetryQueueEntity>(modelBuilder, "TelemetryQueue");
        ConfigureTable<SubAgentEntity>(modelBuilder, "SubAgents");
        ConfigureTable<SshKeyEntity>(modelBuilder, "SSHKeys");
        ConfigureTable<AuditLogEntity>(modelBuilder, "AuditLog");
        ConfigureTable<EnvironmentConfigEntity>(modelBuilder, "EnvironmentConfigs");
        ConfigureTable<EditorStateEntity>(modelBuilder, "EditorState");
        ConfigureTable<LinterRuleEntity>(modelBuilder, "LinterRules");
        ConfigureTable<IapCacheEntity>(modelBuilder, "IAPCache");

        modelBuilder.Entity<UserEntity>().HasIndex(user => user.Email).IsUnique();
        modelBuilder.Entity<ProjectEntity>().HasIndex(project => project.DirectoryPath).IsUnique();
        modelBuilder.Entity<MessageEntity>().HasIndex(message => message.SessionId);
        modelBuilder.Entity<ImplementationPlanEntity>().HasIndex(plan => plan.SessionId);
        modelBuilder.Entity<TodoListEntity>().HasIndex(list => new { list.SessionId, list.PlanId });
        modelBuilder.Entity<ClarifyQuestionEntity>().HasIndex(question => question.SessionId);
    }

    private static void ConfigureTable<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>().ToTable(tableName);
    }

    /// <summary>
    /// SQLite has no native <see cref="DateTimeOffset"/>; EF Core's default text storage cannot be
    /// used in <c>ORDER BY</c> / arithmetic. We round-trip through Unix milliseconds so the column
    /// is a sortable INTEGER while the CLR side stays a <see cref="DateTimeOffset"/>.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
