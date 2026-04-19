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
}
