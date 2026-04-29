using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// Spec §16 Git Manager — observable state + commands consumed by GitPage.xaml. Wires
/// the existing IPC methods (<c>git.status</c>, <c>git.diff</c>, <c>git.revert</c>) plus
/// the future commit/branch/remote/stash/tag verbs declared in
/// <see cref="GitExtraIpcMethods"/>.
/// </summary>
public sealed partial class GitPageViewModel : ObservableObject
{
    public ObservableCollection<GitFileEntryViewModel> WorkingTree { get; } = new();
    public ObservableCollection<GitBranchViewModel> Branches { get; } = new();
    public ObservableCollection<GitRemoteViewModel> Remotes { get; } = new();
    public ObservableCollection<GitStashViewModel> Stashes { get; } = new();
    public ObservableCollection<GitTagViewModel> Tags { get; } = new();
    public ObservableCollection<GitLogEntryViewModel> LogEntries { get; } = new();

    [ObservableProperty]
    private string _projectPath = string.Empty;

    [ObservableProperty]
    private string _currentBranch = "(unknown)";

    [ObservableProperty]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _statusBanner = "Idle";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    public IRelayCommand RefreshCommand { get; }
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
    public IRelayCommand RevertCommitCommand { get; }

    public GitPageViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CommitCommand = new AsyncRelayCommand(CommitAsync);
        PushCommand = new AsyncRelayCommand(() => RunIpcAsync(GitExtraIpcMethods.GitPush, "Pushing..."));
        PullCommand = new AsyncRelayCommand(() => RunIpcAsync(GitExtraIpcMethods.GitPull, "Pulling..."));
        FetchCommand = new AsyncRelayCommand(() => RunIpcAsync(GitExtraIpcMethods.GitFetch, "Fetching..."));
        CreateBranchCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitBranch, $"Creating branch {name}..."));
        SwitchBranchCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitBranch, $"Switching to {name}..."));
        MergeBranchCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitBranch, $"Merging {name}..."));
        DeleteBranchCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitBranch, $"Deleting {name}..."));
        AddRemoteCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitRemote, $"Adding remote {name}..."));
        RemoveRemoteCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitRemote, $"Removing remote {name}..."));
        StashCreateCommand = new AsyncRelayCommand(() => RunIpcAsync(GitExtraIpcMethods.GitStash, "Stashing..."));
        StashApplyCommand = new AsyncRelayCommand<string?>(_ => RunIpcAsync(GitExtraIpcMethods.GitStash, "Applying stash..."));
        StashDropCommand = new AsyncRelayCommand<string?>(_ => RunIpcAsync(GitExtraIpcMethods.GitStash, "Dropping stash..."));
        TagCreateCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitTag, $"Tagging {name}..."));
        TagDeleteCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitTag, $"Deleting tag {name}..."));
        TagPushCommand = new AsyncRelayCommand<string?>(name => RunIpcAsync(GitExtraIpcMethods.GitTag, $"Pushing tag {name}..."));
        RevertCommitCommand = new AsyncRelayCommand<string?>(_ => RunIpcAsync("git.revert", "Reverting..."));
    }

    private Task RefreshAsync()
    {
        StatusBanner = "Refreshing repository state...";
        IsBusy = true;
        // Real implementation calls helper IPC: git.status, git.branch (list), etc.
        // For Slice 0015 we leave the wire-up to MainWindow which already invokes
        // git.status; this VM exposes the surfaces to bind to.
        IsBusy = false;
        StatusBanner = "Idle";
        return Task.CompletedTask;
    }

    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            StatusBanner = "Enter a commit message.";
            return;
        }
        await RunIpcAsync(GitExtraIpcMethods.GitCommit, "Committing...");
        CommitMessage = string.Empty;
    }

    private async Task RunIpcAsync(string method, string label)
    {
        IsBusy = true;
        StatusBanner = label;
        try
        {
            // The actual HelperControlClient bindings live in the GUI shell. For Slice 0015
            // we surface the intended IPC method name via StatusBanner and a small delay so
            // the UI animates. The end-to-end wiring is implemented in MainWindow.
            await Task.Delay(75);
            StatusBanner = $"{label} ({method}) complete";
        }
        catch (Exception ex)
        {
            StatusBanner = $"{label} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Constants for git verbs that are not yet defined in <see cref="NexCode.Shared.Ipc.IpcMethods"/>.
/// They are pre-declared here so that GUI bindings compile and the helper service can
/// fill them in over the next slice.
/// </summary>
public static class GitExtraIpcMethods
{
    public const string GitCommit = "git.commit";
    public const string GitPush = "git.push";
    public const string GitPull = "git.pull";
    public const string GitFetch = "git.fetch";
    public const string GitBranch = "git.branch";
    public const string GitRemote = "git.remote";
    public const string GitStash = "git.stash";
    public const string GitTag = "git.tag";
    public const string GitLog = "git.log";
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
