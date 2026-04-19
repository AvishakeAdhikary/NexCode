namespace NexCode.Shared.Models;

public enum SessionMode
{
    Plan,
    Code,
    Debug,
    Ask
}

public enum ExecutionMode
{
    Local,
    Remote,
    Cloud
}

public enum PermissionLevel
{
    Default,
    Full
}

public enum SubscriptionTier
{
    Free,
    Pro,
    Team,
    Enterprise,
    SuperUser
}

public enum PlanStatus
{
    Draft,
    PendingConfirmation,
    Confirmed,
    Rejected,
    Completed,
    Deleted
}

public enum TodoItemStatus
{
    Pending,
    InProgress,
    Done,
    Skipped
}

public enum MemoryScope
{
    Global,
    Project,
    Session
}

public enum ServiceHealthState
{
    Starting,
    Healthy,
    Degraded,
    Stopped
}
