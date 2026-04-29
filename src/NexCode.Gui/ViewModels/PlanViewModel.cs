using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// View model for a plan artifact card (spec §10). Wraps a single plan and exposes
/// the confirm/reject/request-changes commands surfaced inline by the message bubble.
/// </summary>
public sealed partial class PlanViewModel : ObservableViewModelBase
{
    public PlanViewModel()
        : this(Guid.Empty, "Untitled plan", string.Empty, PlanStatus.Draft, version: 1)
    {
    }

    public PlanViewModel(Guid planId, string title, string content, PlanStatus status, int version)
    {
        PlanId = planId;
        _title = title;
        _content = content;
        _status = status;
        _version = version;
    }

    public Guid PlanId { get; private set; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private PlanStatus _status;

    [ObservableProperty]
    private int _version;

    [ObservableProperty]
    private string? _changeRequestNotes;

    [ObservableProperty]
    private string? _rejectionReason;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Compact preview of the plan body (~150 chars) shown when collapsed.</summary>
    public string ContentPreview =>
        Content.Length <= 160
            ? Content
            : Content[..157] + "...";

    public bool IsPendingConfirmation => Status == PlanStatus.PendingConfirmation;

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(ContentPreview));

    partial void OnStatusChanged(PlanStatus value) => OnPropertyChanged(nameof(IsPendingConfirmation));

    /// <summary>Raised so the host (ShellViewModel) can dispatch the IPC call.</summary>
    public event EventHandler<PlanDecision>? DecisionRequested;

    [RelayCommand]
    private void Confirm()
    {
        DecisionRequested?.Invoke(this, new PlanDecision(PlanId, PlanDecisionKind.Confirm, Notes: null));
    }

    [RelayCommand]
    private void Reject()
    {
        DecisionRequested?.Invoke(this, new PlanDecision(PlanId, PlanDecisionKind.Reject, Notes: RejectionReason));
    }

    [RelayCommand]
    private void RequestChanges()
    {
        DecisionRequested?.Invoke(this, new PlanDecision(PlanId, PlanDecisionKind.RequestChanges, Notes: ChangeRequestNotes));
    }
}

public enum PlanDecisionKind
{
    Confirm,
    Reject,
    RequestChanges
}

public sealed record PlanDecision(Guid PlanId, PlanDecisionKind Kind, string? Notes);
