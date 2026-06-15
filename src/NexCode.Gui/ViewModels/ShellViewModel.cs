using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// Top-level shell view model. Owns the active session, the project list, the
/// account snapshot, and pipes service events from <see cref="HelperControlClient"/>
/// onto the UI dispatcher.
/// </summary>
public sealed partial class ShellViewModel : ObservableViewModelBase
{
    private readonly HelperControlClient _helperControlClient;
    private DispatcherQueue? _dispatcher;

    public ShellViewModel(HelperControlClient helperControlClient)
    {
        _helperControlClient = helperControlClient;
        Projects = [];
        Sessions = [];
    }

    public ObservableCollection<ProjectViewModel> Projects { get; }

    public ObservableCollection<SessionViewModel> Sessions { get; }

    [ObservableProperty]
    private SessionViewModel? _active;

    [ObservableProperty]
    private string _accountStatusLabel = "Auth: unknown";

    [ObservableProperty]
    private string _subscriptionLabel = "Tier: unknown";

    [ObservableProperty]
    private string _helperStatusLabel = "Not checked";

    [ObservableProperty]
    private bool _helperBusy;

    [ObservableProperty]
    private bool _isAuthGateVisible;

    [ObservableProperty]
    private bool _isSubscriptionGateVisible;

    [ObservableProperty]
    private SubscriptionTier _activeGateRequiredTier = SubscriptionTier.Pro;

    [ObservableProperty]
    private string _activeGateFeatureName = string.Empty;

    [ObservableProperty]
    private string _activeGateDetail = string.Empty;

    /// <summary>Initialise dispatcher-bound state. Called once after the shell page is loaded.</summary>
    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Raised when a nav-rail destination is chosen. The host (MainWindow) maps the
    /// destination to a page and navigates the shell frame.</summary>
    public event EventHandler<ShellDestination>? NavigationRequested;

    public event EventHandler<string>? SignInRequested;
    public event EventHandler? RestorePurchasesRequested;
    public event EventHandler<NewSessionRequest>? NewSessionRequested;
    public event EventHandler<SubscriptionTier>? UpgradeRequested;

    [RelayCommand]
    private void NewChat()
    {
        NewSessionRequested?.Invoke(this, new NewSessionRequest(
            ProjectPath: Projects.FirstOrDefault()?.DirectoryPath ?? string.Empty,
            Mode: SessionMode.Code,
            ExecutionMode: ExecutionMode.Local,
            SandboxEnabled: false));
    }

    [RelayCommand]
    private void OpenSearch() => NavigationRequested?.Invoke(this, ShellDestination.Search);

    [RelayCommand]
    private void OpenHistory() => NavigationRequested?.Invoke(this, ShellDestination.History);

    [RelayCommand]
    private void OpenPlans() => NavigationRequested?.Invoke(this, ShellDestination.Plans);

    [RelayCommand]
    private void OpenMemories() => NavigationRequested?.Invoke(this, ShellDestination.Memories);

    [RelayCommand]
    private void OpenPlugins() => NavigationRequested?.Invoke(this, ShellDestination.Plugins);

    [RelayCommand]
    private void OpenAutomations() => NavigationRequested?.Invoke(this, ShellDestination.Automations);

    [RelayCommand]
    private void OpenSettings() => NavigationRequested?.Invoke(this, ShellDestination.Settings);

    [RelayCommand]
    private void SignIn() => SignInRequested?.Invoke(this, "interactive");

    [RelayCommand]
    private void RestorePurchases() => RestorePurchasesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DismissSubscriptionGate()
    {
        IsSubscriptionGateVisible = false;
    }

    [RelayCommand]
    private void UpgradeToPro() => UpgradeRequested?.Invoke(this, SubscriptionTier.Pro);

    [RelayCommand]
    private void UpgradeToTeam() => UpgradeRequested?.Invoke(this, SubscriptionTier.Team);

    public void ShowSubscriptionGate(SubscriptionTier required, string feature, string detail)
    {
        ActiveGateRequiredTier = required;
        ActiveGateFeatureName = feature;
        ActiveGateDetail = detail;
        IsSubscriptionGateVisible = true;
    }

    public void ApplyAccountSnapshot(AccountSnapshotPayload snapshot)
    {
        var auth = snapshot.Auth;
        var sub = snapshot.Subscription;
        AccountStatusLabel = auth.IsAuthenticated
            ? $"Auth: {auth.UserEmail}"
            : auth.RequiresAuthentication
                ? "Auth: sign-in required"
                : "Auth: unknown";
        SubscriptionLabel = $"Tier: {sub.Tier}";
        IsAuthGateVisible = auth.RequiresAuthentication;
    }

    public void ApplyHelperStatus(string label, bool busy)
    {
        HelperStatusLabel = label;
        HelperBusy = busy;
    }

    public HelperControlClient HelperControlClient => _helperControlClient;

    public DispatcherQueue? Dispatcher => _dispatcher;
}

public sealed record NewSessionRequest(
    string ProjectPath,
    SessionMode Mode,
    ExecutionMode ExecutionMode,
    bool SandboxEnabled);

/// <summary>Nav-rail destinations reachable from the shell. Mapped to pages by the host.</summary>
public enum ShellDestination
{
    Search,
    History,
    Plans,
    Memories,
    Plugins,
    Automations,
    Settings,
}
