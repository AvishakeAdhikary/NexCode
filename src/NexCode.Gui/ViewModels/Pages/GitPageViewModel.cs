using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// Spec §16 Git Manager — observable state + commands consumed by GitPage.xaml. Only the
/// backend-supported verbs are wired: <c>git.status</c>, <c>git.diff</c>, and
/// <c>git.revert</c> via <see cref="HelperControlClient"/>. Commit/push/pull/fetch/branch/
/// remote/stash/tag have no service handler yet, so their commands report that they are
/// unsupported rather than pretending to run.
/// </summary>
public sealed partial class GitPageViewModel : ObservableObject
{
    private const string DefaultProjectPath = @"C:\Projects\NexCode";
    private const string NotSupportedMessage = "Not supported yet (no backend handler).";

    public ObservableCollection<GitFileEntryViewModel> WorkingTree { get; } = new();
    public ObservableCollection<GitBranchViewModel> Branches { get; } = new();
    public ObservableCollection<GitRemoteViewModel> Remotes { get; } = new();
    public ObservableCollection<GitStashViewModel> Stashes { get; } = new();
    public ObservableCollection<GitTagViewModel> Tags { get; } = new();
    public ObservableCollection<GitLogEntryViewModel> LogEntries { get; } = new();

    [ObservableProperty]
    private string _projectPath = DefaultProjectPath;

    [ObservableProperty]
    private string _currentBranch = "(unknown)";

    [ObservableProperty]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _statusBanner = "Idle";

    [ObservableProperty]
    private string _unifiedDiff = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    private HelperControlClient? _client;

    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand DiffCommand { get; }
    public IRelayCommand<string?> RevertCommitCommand { get; }
    public IRelayCommand CommitCommand { get; }
    public IRelayCommand PushCommand { get; }
    public IRelayCommand PullCommand { get; }
    public IRelayCommand FetchCommand { get; }
    public IRelayCommand CreateBranchCommand { get; }
    public IRelayCommand SwitchBranchCommand { get; }
    public IRelayCommand MergeBranchCommand { get; }
    public IRelayCommand DeleteBranchCommand { get; }
    public IRelayCommand AddRemoteCommand { get; }
    public IRelayCommand RemoveRemoteCommand { get; }
    public IRelayCommand StashCreateCommand { get; }
    public IRelayCommand StashApplyCommand { get; }
    public IRelayCommand StashDropCommand { get; }
    public IRelayCommand TagCreateCommand { get; }
    public IRelayCommand TagDeleteCommand { get; }
    public IRelayCommand TagPushCommand { get; }

    public GitPageViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        DiffCommand = new AsyncRelayCommand(DiffAsync);
        RevertCommitCommand = new AsyncRelayCommand<string?>(RevertAsync);

        // No service handler exists for these verbs yet. Keep them disabled and surface the
        // reason instead of inventing IPC methods.
        CommitCommand = new RelayCommand(MarkUnsupported, () => false);
        PushCommand = new RelayCommand(MarkUnsupported, () => false);
        PullCommand = new RelayCommand(MarkUnsupported, () => false);
        FetchCommand = new RelayCommand(MarkUnsupported, () => false);
        CreateBranchCommand = new RelayCommand(MarkUnsupported, () => false);
        SwitchBranchCommand = new RelayCommand(MarkUnsupported, () => false);
        MergeBranchCommand = new RelayCommand(MarkUnsupported, () => false);
        DeleteBranchCommand = new RelayCommand(MarkUnsupported, () => false);
        AddRemoteCommand = new RelayCommand(MarkUnsupported, () => false);
        RemoveRemoteCommand = new RelayCommand(MarkUnsupported, () => false);
        StashCreateCommand = new RelayCommand(MarkUnsupported, () => false);
        StashApplyCommand = new RelayCommand(MarkUnsupported, () => false);
        StashDropCommand = new RelayCommand(MarkUnsupported, () => false);
        TagCreateCommand = new RelayCommand(MarkUnsupported, () => false);
        TagDeleteCommand = new RelayCommand(MarkUnsupported, () => false);
        TagPushCommand = new RelayCommand(MarkUnsupported, () => false);
    }

    /// <summary>Called by the page on activation: binds the helper transport and refreshes.</summary>
    public async Task InitializeAsync(HelperControlClient client)
    {
        _client = client;
        await RefreshAsync();
    }

    private string EffectiveProjectPath =>
        string.IsNullOrWhiteSpace(ProjectPath) ? DefaultProjectPath : ProjectPath;

    private async Task RefreshAsync()
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        StatusBanner = "Refreshing repository state...";
        try
        {
            var status = await _client.GetGitStatusAsync(EffectiveProjectPath);

            WorkingTree.Clear();
            foreach (var file in status.Files)
            {
                WorkingTree.Add(new GitFileEntryViewModel
                {
                    Path = file.Path,
                    IndexState = file.IndexState,
                    WorkingTreeState = file.WorkingTreeState,
                    IsStaged = !string.IsNullOrWhiteSpace(file.IndexState) && file.IndexState != " ",
                });
            }

            CurrentBranch = status.CurrentBranch ?? "(detached)";
            StatusBanner = status.IsRepository
                ? $"{WorkingTree.Count} change(s) on {CurrentBranch}."
                : "Not a git repository.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiffAsync()
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        StatusBanner = "Loading diff...";
        try
        {
            var diff = await _client.GetGitDiffAsync(EffectiveProjectPath);
            UnifiedDiff = diff.UnifiedDiff;
            StatusBanner = diff.IsRepository
                ? (string.IsNullOrWhiteSpace(diff.UnifiedDiff) ? "No changes to diff." : "Diff loaded.")
                : "Not a git repository.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Diff failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RevertAsync(string? checkpointHash)
    {
        if (_client is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(checkpointHash))
        {
            StatusBanner = "Enter a checkpoint commit hash to revert to.";
            return;
        }

        IsBusy = true;
        StatusBanner = $"Reverting to {checkpointHash}...";
        try
        {
            var result = await _client.RevertToCheckpointAsync(EffectiveProjectPath, checkpointHash);
            StatusBanner = result.Success
                ? result.Message ?? $"Reverted to {checkpointHash}."
                : result.Message ?? "Revert failed.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Revert failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void MarkUnsupported() => StatusBanner = NotSupportedMessage;
}

public sealed partial class GitFileEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _indexState = string.Empty;
    [ObservableProperty] private string _workingTreeState = string.Empty;
    [ObservableProperty] private bool _isStaged;
}

public sealed partial class GitBranchViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private string? _upstream;
    [ObservableProperty] private int _ahead;
    [ObservableProperty] private int _behind;
}

public sealed partial class GitRemoteViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _fetchUrl = string.Empty;
    [ObservableProperty] private string? _pushUrl;
}

public sealed partial class GitStashViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private DateTimeOffset _createdAt;
}

public sealed partial class GitTagViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _commit;
    [ObservableProperty] private string? _message;
}

public sealed partial class GitLogEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _commit = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private DateTimeOffset _timestamp;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _graph = string.Empty;
}
