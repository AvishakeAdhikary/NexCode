using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels;

public enum PermissionDecisionKind
{
    AllowOnce,
    AllowForSession,
    DenyOnce,
    DenyAlways
}

/// <summary>
/// Permission prompt card view model (spec §26).
/// </summary>
public sealed partial class PermissionPromptViewModel : ObservableViewModelBase
{
    public PermissionPromptViewModel()
        : this(Guid.Empty, string.Empty, "Tool", "Tool description", "{}", levelRequired: "Default")
    {
    }

    public PermissionPromptViewModel(
        Guid sessionId,
        string callId,
        string toolName,
        string description,
        string argumentsPreview,
        string levelRequired)
    {
        SessionId = sessionId;
        CallId = callId;
        _toolName = toolName;
        _description = description;
        _argumentsPreview = argumentsPreview;
        _levelRequired = levelRequired;
    }

    public Guid SessionId { get; }

    public string CallId { get; }

    [ObservableProperty]
    private string _toolName;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _argumentsPreview;

    [ObservableProperty]
    private string _levelRequired;

    [ObservableProperty]
    private bool _fullAccessLockShown;

    public event EventHandler<PermissionDecisionKind>? DecisionRequested;

    [RelayCommand]
    private void AllowOnce() => DecisionRequested?.Invoke(this, PermissionDecisionKind.AllowOnce);

    [RelayCommand]
    private void AllowForSession() => DecisionRequested?.Invoke(this, PermissionDecisionKind.AllowForSession);

    [RelayCommand]
    private void DenyOnce() => DecisionRequested?.Invoke(this, PermissionDecisionKind.DenyOnce);

    [RelayCommand]
    private void DenyAlways() => DecisionRequested?.Invoke(this, PermissionDecisionKind.DenyAlways);
}
