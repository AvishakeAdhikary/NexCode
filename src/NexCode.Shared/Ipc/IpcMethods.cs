namespace NexCode.Shared.Ipc;

public static class IpcMethods
{
    public const string ServiceHealth = "service.health";
    public const string ServicePollEvents = "service.poll_events";
    public const string AccountGetSnapshot = "account.get_snapshot";
    public const string AccountSignIn = "account.sign_in";
    public const string AccountRefreshSubscription = "account.refresh_subscription";
    public const string SessionCreate = "session.create";
    public const string SessionSendMessage = "session.send_message";
    public const string SessionCancel = "session.cancel";
    public const string PlanConfirm = "plan.confirm";
    public const string PlanReject = "plan.reject";

    // Slice 0011 additions
    public const string ProviderList = "provider.list";
    public const string ProviderUpsert = "provider.upsert";
    public const string ProviderRemove = "provider.remove";
    public const string ProviderSetDefault = "provider.set_default";
    public const string PermissionRespond = "permission.respond";
    public const string GitStatus = "git.status";
    public const string GitDiff = "git.diff";
    public const string GitRevert = "git.revert";
}
