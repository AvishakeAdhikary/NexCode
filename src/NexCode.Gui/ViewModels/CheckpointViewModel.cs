using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels;

public enum CheckpointActionKind
{
    ViewDiff,
    Revert,
    ContinueFromHere,
    Retry
}

/// <summary>
/// Git checkpoint card view model (spec §15.4). Wired commands raise an event;
/// actual diff opening is deferred to slice 0015.
/// </summary>
public sealed partial class CheckpointViewModel : ObservableViewModelBase
{
    public CheckpointViewModel()
        : this(Guid.Empty, string.Empty, string.Empty, [])
    {
    }

    public CheckpointViewModel(Guid sessionId, string commitHash, string diffSummary, IReadOnlyList<string> filesChanged)
    {
        SessionId = sessionId;
        CommitHash = commitHash;
        _diffSummary = diffSummary;
        FilesChanged = new ObservableCollection<string>(filesChanged);
    }

    public Guid SessionId { get; }

    public string CommitHash { get; }

    public string ShortHash =>
        string.IsNullOrEmpty(CommitHash) ? string.Empty : CommitHash[..Math.Min(7, CommitHash.Length)];

    [ObservableProperty]
    private string _diffSummary;

    public ObservableCollection<string> FilesChanged { get; }

    public event EventHandler<CheckpointActionKind>? ActionRequested;

    [RelayCommand]
    private void ViewDiff() => ActionRequested?.Invoke(this, CheckpointActionKind.ViewDiff);

    [RelayCommand]
    private void Revert() => ActionRequested?.Invoke(this, CheckpointActionKind.Revert);

    [RelayCommand]
    private void ContinueFromHere() => ActionRequested?.Invoke(this, CheckpointActionKind.ContinueFromHere);

    [RelayCommand]
    private void Retry() => ActionRequested?.Invoke(this, CheckpointActionKind.Retry);
}
