using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// Per-session view model: holds the transcript, current plan/todo artifacts, and
/// the session-level toggles surfaced by the <c>SessionHeaderBar</c>.
/// </summary>
public sealed partial class SessionViewModel : ObservableViewModelBase
{
    public SessionViewModel()
    {
        Messages = [];
    }

    [ObservableProperty]
    private Guid _sessionId;

    [ObservableProperty]
    private SessionMode _mode = SessionMode.Code;

    [ObservableProperty]
    private string _personality = "Default";

    [ObservableProperty]
    private string _modelDisplayName = "default";

    [ObservableProperty]
    private string? _modelProviderKey;

    [ObservableProperty]
    private ExecutionMode _executionMode = ExecutionMode.Local;

    [ObservableProperty]
    private bool _sandboxEnabled;

    [ObservableProperty]
    private string _shellName = "pwsh";

    [ObservableProperty]
    private bool _turnInProgress;

    [ObservableProperty]
    private string? _statusLine;

    [ObservableProperty]
    private PlanViewModel? _currentPlan;

    [ObservableProperty]
    private TodoListViewModel? _todoList;

    [ObservableProperty]
    private string? _composerText;

    [ObservableProperty]
    private MessageViewModel? _streamingAssistantMessage;

    /// <summary>
    /// Transcript entries. Uses a plain ObservableCollection in this slice;
    /// <c>ShellPage</c> is wired so the implementation can be swapped for an
    /// IncrementalLoadingCollection in a follow-up without touching binding code.
    /// </summary>
    public ObservableCollection<MessageViewModel> Messages { get; }

    public bool CanCompose =>
        SessionId != Guid.Empty && !TurnInProgress;

    partial void OnTurnInProgressChanged(bool value) => OnPropertyChanged(nameof(CanCompose));

    partial void OnSessionIdChanged(Guid value) => OnPropertyChanged(nameof(CanCompose));

    public event EventHandler<string>? SendMessageRequested;
    public event EventHandler? StopRequested;

    [RelayCommand]
    private void SendMessage()
    {
        var text = ComposerText?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        ComposerText = string.Empty;
        SendMessageRequested?.Invoke(this, text);
    }

    [RelayCommand]
    private void Stop()
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }
}
