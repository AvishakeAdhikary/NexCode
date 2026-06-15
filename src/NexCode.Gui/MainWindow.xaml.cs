using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using NexCode.Gui.Auth;
using NexCode.Gui.Infrastructure;
using NexCode.Gui.Pages;
using NexCode.Gui.Services;
using NexCode.Gui.Store;
using NexCode.Gui.ViewModels;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using NexCode.Shared.Models;
using Windows.Graphics;
using WinRT.Interop;

namespace NexCode.Gui;

public sealed partial class MainWindow : Window
{
    private readonly HelperControlClient _helperControlClient;
    private readonly ShellViewModel _shellViewModel;
    private readonly ThemeService _themeService;
    private readonly AuthGatePresentationState _authGateState = new();
    private readonly StorePurchaseService _storePurchaseService = new();
    private readonly WindowSizeConstraintHelper _windowSizeConstraintHelper;
    private readonly CancellationTokenSource _eventPollingCts = new();
    private readonly StringBuilder _streamingAssistantBuffer = new();
    private readonly Dictionary<string, string> _activeToolNamesByCallId = [];
    private long _lastServiceEventSequence;
    private AccountSnapshotPayload? _latestAccountSnapshot;

    public MainWindow()
    {
        InitializeComponent();

        var services = ((App)Application.Current).Services;
        _helperControlClient = services.GetRequiredService<HelperControlClient>();
        _shellViewModel = services.GetRequiredService<ShellViewModel>();
        _themeService = services.GetRequiredService<ThemeService>();

        AppWindow.Title = "NexCode";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.ResizeClient(new SizeInt32(1440, 900));
        _windowSizeConstraintHelper = WindowSizeConstraintHelper.Attach(this, 800, 600);
        Closed += MainWindow_Closed;

        // Extend the Mica Alt backdrop up under a custom title bar so the window
        // reads as a single Fluent surface instead of a flat bar over content.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        // Theme service binds to the root content for RequestedTheme propagation.
        _themeService.AttachRoot(RootGrid);

        // Wire shell view model events to legacy helper plumbing.
        _shellViewModel.SignInRequested += async (_, _) => await DoSignInAsync();
        _shellViewModel.RestorePurchasesRequested += async (_, _) => await DoRefreshSubscriptionAsync();
        _shellViewModel.NewSessionRequested += async (_, req) => await CreateSessionAsync(req);
        _shellViewModel.UpgradeRequested += async (_, tier) => await StartStorePurchaseAsync(MapTierToProductId(tier));

        // Auth gate overlay
        AuthGate.SignInRequested += async (_, _) => await DoSignInAsync();
        AuthGate.RefreshRequested += async (_, _) => await RefreshHelperStatusAsync();

        // Subscription gate
        SubscriptionGate.UpgradeProRequested += async (_, _) => await StartStorePurchaseAsync("nexcode_pro_monthly");
        SubscriptionGate.UpgradeTeamRequested += async (_, _) => await StartStorePurchaseAsync("nexcode_team_monthly");
        SubscriptionGate.RestoreRequested += async (_, _) => await DoRefreshSubscriptionAsync();
        SubscriptionGate.DismissRequested += (_, _) => SubscriptionGate.Visibility = Visibility.Collapsed;

        // Nav-rail destinations: map to pages and drive the shell frame.
        _shellViewModel.NavigationRequested += OnNavigationRequested;
        ShellFrame.Navigated += OnShellFrameNavigated;

        // Navigate to ShellPage and pass the view model.
        ShellFrame.Navigate(typeof(ShellPage), _shellViewModel);

        ApplyAuthGatePresentation();
        _ = RefreshHelperStatusAsync();
        _ = PollServiceEventsAsync(_eventPollingCts.Token);
    }

    private async Task RefreshHelperStatusAsync()
    {
        _shellViewModel.ApplyHelperStatus("Checking...", busy: true);

        try
        {
            var status = await _helperControlClient.GetHealthAsync();
            var account = await _helperControlClient.GetAccountSnapshotAsync();
            _shellViewModel.ApplyHelperStatus($"{status.State} ({status.ActiveSessions} sessions)", busy: false);
            UpdateAccountState(account);
        }
        catch (Exception ex)
        {
            _shellViewModel.ApplyHelperStatus($"Unavailable: {ex.GetType().Name}", busy: false);
            _authGateState.RecordRefreshFailure(ex.Message);
        }
        finally
        {
            ApplyAuthGatePresentation();
        }
    }

    private void UpdateAccountState(AccountSnapshotPayload accountSnapshot)
    {
        _latestAccountSnapshot = accountSnapshot;
        _shellViewModel.ApplyAccountSnapshot(accountSnapshot);
        _authGateState.ApplySnapshot(accountSnapshot, DateTimeOffset.UtcNow);
        ApplyAuthGatePresentation();
    }

    private async Task DoSignInAsync()
    {
        _authGateState.StartInteractiveSignIn(DateTimeOffset.UtcNow);
        ApplyAuthGatePresentation();
        try
        {
            var snapshot = await _helperControlClient.SignInAsync();
            UpdateAccountState(snapshot);
        }
        catch (Exception ex)
        {
            _authGateState.RecordSignInFailure(ex.Message, DateTimeOffset.UtcNow);
            ApplyAuthGatePresentation();
        }
    }

    private async Task DoRefreshSubscriptionAsync()
    {
        try
        {
            var response = await _helperControlClient.RefreshSubscriptionAsync();
            UpdateAccountState(response.Snapshot);
        }
        catch
        {
            ApplyAuthGatePresentation();
        }
    }

    private async Task CreateSessionAsync(NewSessionRequest req)
    {
        if (_latestAccountSnapshot is null)
        {
            await RefreshHelperStatusAsync();
            if (_latestAccountSnapshot is null) return;
        }
        var capabilities = _latestAccountSnapshot.Capabilities;
        if (req.SandboxEnabled && !capabilities.CanUseSandbox)
        {
            _shellViewModel.ShowSubscriptionGate(SubscriptionTier.Pro, "Sandbox mode", "Sandbox requires NexCode Pro or higher.");
            ShowSubscriptionGateInUi();
            return;
        }
        if (req.ExecutionMode == ExecutionMode.Remote && !capabilities.CanUseRemoteExecution)
        {
            _shellViewModel.ShowSubscriptionGate(SubscriptionTier.Pro, "Remote execution", "Remote execution requires NexCode Pro or higher.");
            ShowSubscriptionGateInUi();
            return;
        }
        if (req.ExecutionMode == ExecutionMode.Cloud && !capabilities.CanUseCloudExecution)
        {
            _shellViewModel.ShowSubscriptionGate(SubscriptionTier.Team, "Cloud execution", "Cloud execution requires NexCode Team or higher.");
            ShowSubscriptionGateInUi();
            return;
        }

        var request = new SessionCreateRequest(
            ProjectPath: string.IsNullOrEmpty(req.ProjectPath) ? "C:\\Projects\\NexCode" : req.ProjectPath,
            Mode: req.Mode,
            ExecutionMode: req.ExecutionMode,
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: req.SandboxEnabled);

        try
        {
            var response = await _helperControlClient.CreateSessionAsync(request);
            var session = new SessionViewModel
            {
                SessionId = response.SessionId,
                Mode = req.Mode,
                ExecutionMode = req.ExecutionMode,
                SandboxEnabled = req.SandboxEnabled,
            };
            _shellViewModel.Sessions.Add(session);
            _shellViewModel.Active = session;
        }
        catch (HelperRequestException ex) when (ex.Code == -32021)
        {
            _shellViewModel.ShowSubscriptionGate(SubscriptionTier.Pro, "Paid feature", ex.Message);
            ShowSubscriptionGateInUi();
        }
        catch
        {
            // Surfaced via helper status label refresh
        }
    }

    private void ShowSubscriptionGateInUi()
    {
        SubscriptionGate.Configure(
            _shellViewModel.ActiveGateRequiredTier,
            _shellViewModel.ActiveGateFeatureName,
            _shellViewModel.ActiveGateDetail,
            statusMessage: null);
        SubscriptionGate.Show();
    }

    private async Task StartStorePurchaseAsync(string productId)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var result = await _storePurchaseService.RequestPurchaseAsync(productId, hwnd);
            if (result.ShouldRefreshSubscription)
            {
                await DoRefreshSubscriptionAsync();
            }
        }
        catch
        {
        }
    }

    private async Task PollServiceEventsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var response = await _helperControlClient.PollEventsAsync(_lastServiceEventSequence, cancellationToken);
                _lastServiceEventSequence = response.LatestSequence;
                _authGateState.RecordPollSuccess(DateTimeOffset.UtcNow);
                foreach (var serviceEvent in response.Events)
                {
                    ProcessServiceEvent(serviceEvent);
                }
                ApplyAuthGatePresentation();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _authGateState.RecordPollFailure(ex.Message);
                ApplyAuthGatePresentation();
            }
        }
    }

    private void ProcessServiceEvent(ServiceEventEnvelope serviceEvent)
    {
        var active = _shellViewModel.Active;
        switch (serviceEvent.EventType)
        {
            case ServiceEventTypes.AuthRequired:
            {
                var payload = serviceEvent.Payload.Deserialize<AuthRequiredEventPayload>(JsonSerialization.Options);
                if (payload is null) return;
                _authGateState.ApplyAuthRequiredEvent(payload, serviceEvent.Timestamp);
                break;
            }
            case ServiceEventTypes.AuthSuccess:
            {
                var payload = serviceEvent.Payload.Deserialize<AuthSuccessEventPayload>(JsonSerialization.Options);
                _authGateState.ApplyAuthSuccessEvent(payload, serviceEvent.Timestamp);
                _ = RefreshHelperStatusAsync();
                break;
            }
            case ServiceEventTypes.SessionStart:
            {
                var payload = serviceEvent.Payload.Deserialize<SessionStartEventPayload>(JsonSerialization.Options);
                if (payload is null || active is null || active.SessionId != payload.SessionId) return;
                active.TurnInProgress = true;
                _streamingAssistantBuffer.Clear();
                var streaming = new MessageViewModel(MessageRole.Assistant, MessageKind.Text, string.Empty)
                {
                    IsStreaming = true,
                };
                active.StreamingAssistantMessage = streaming;
                EnqueueOnDispatcher(() => active.Messages.Add(streaming));
                break;
            }
            case ServiceEventTypes.Token:
            {
                var payload = serviceEvent.Payload.Deserialize<TokenEventPayload>(JsonSerialization.Options);
                if (payload is null || active is null || active.SessionId != payload.SessionId) return;
                _streamingAssistantBuffer.Append(payload.Content);
                var snapshot = _streamingAssistantBuffer.ToString();
                EnqueueOnDispatcher(() =>
                {
                    if (active.StreamingAssistantMessage is { } streaming)
                    {
                        streaming.Content = snapshot;
                    }
                });
                break;
            }
            case ServiceEventTypes.SessionEnd:
            {
                var payload = serviceEvent.Payload.Deserialize<SessionEndEventPayload>(JsonSerialization.Options);
                if (payload is null || active is null || active.SessionId != payload.SessionId) return;
                EnqueueOnDispatcher(() =>
                {
                    if (active.StreamingAssistantMessage is { } streaming)
                    {
                        streaming.IsStreaming = false;
                    }
                    active.StreamingAssistantMessage = null;
                    active.TurnInProgress = false;
                });
                _streamingAssistantBuffer.Clear();
                break;
            }
            case ServiceEventTypes.ToolCall:
            {
                var payload = serviceEvent.Payload.Deserialize<ToolCallEventPayload>(JsonSerialization.Options);
                if (payload is null || active is null || active.SessionId != payload.SessionId) return;
                _activeToolNamesByCallId[payload.CallId] = payload.ToolName;
                EnqueueOnDispatcher(() =>
                    active.Messages.Add(new MessageViewModel(MessageRole.Tool, MessageKind.ToolCall, $"{payload.ToolName}: {payload.ArgumentsJson}")
                    {
                        ToolName = payload.ToolName
                    }));
                break;
            }
            case ServiceEventTypes.ToolResult:
            {
                var payload = serviceEvent.Payload.Deserialize<ToolResultEventPayload>(JsonSerialization.Options);
                if (payload is null || active is null || active.SessionId != payload.SessionId) return;
                _activeToolNamesByCallId.TryGetValue(payload.CallId, out var name);
                EnqueueOnDispatcher(() =>
                    active.Messages.Add(new MessageViewModel(MessageRole.Tool, MessageKind.ToolResult, payload.ResultJson)
                    {
                        ToolName = name
                    }));
                break;
            }
            case ServiceEventTypes.Checkpoint:
            {
                var payload = serviceEvent.Payload.Deserialize<CheckpointEventPayload>(JsonSerialization.Options);
                if (payload is null || active is null || active.SessionId != payload.SessionId) return;
                var hash = payload.GitCommitHash ?? payload.CheckpointId.ToString();
                var checkpoint = new CheckpointViewModel(payload.SessionId, hash, payload.DiffSummary, payload.FilesChanged);
                EnqueueOnDispatcher(() =>
                    active.Messages.Add(new MessageViewModel(MessageRole.System, MessageKind.Checkpoint, payload.DiffSummary)
                    {
                        Artifact = checkpoint
                    }));
                break;
            }
        }
    }

    private void ApplyAuthGatePresentation()
    {
        var presentation = _authGateState.Build();
        AuthGate.ApplyPresentation(presentation);
        _shellViewModel.IsAuthGateVisible = presentation.IsOverlayVisible;
    }

    private void EnqueueOnDispatcher(Action action)
    {
        if (!DispatcherQueue.TryEnqueue(() => action()))
        {
            action();
        }
    }

    private static string MapTierToProductId(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Pro => "nexcode_pro_monthly",
        SubscriptionTier.Team => "nexcode_team_monthly",
        _ => "nexcode_pro_monthly"
    };

    private void OnNavigationRequested(object? sender, ShellDestination destination)
    {
        var pageType = destination switch
        {
            ShellDestination.Search => typeof(SearchPage),
            ShellDestination.History => typeof(HistoryPage),
            ShellDestination.Plans => typeof(PlansPage),
            ShellDestination.Memories => typeof(MemoriesPage),
            ShellDestination.Plugins => typeof(PluginsPage),
            ShellDestination.Automations => typeof(AutomationsPage),
            ShellDestination.Settings => typeof(SettingsPage),
            _ => null
        };

        if (pageType is null || ShellFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ShellFrame.Navigate(pageType);

        // Keep only the chat shell on the back stack so Back always returns to chat
        // rather than chaining between secondary pages.
        while (ShellFrame.BackStack.Count > 1)
        {
            ShellFrame.BackStack.RemoveAt(ShellFrame.BackStack.Count - 1);
        }
    }

    private void OnShellFrameNavigated(object sender, NavigationEventArgs e)
    {
        BackButton.Visibility = ShellFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShellFrame.CanGoBack)
        {
            ShellFrame.GoBack();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _eventPollingCts.Cancel();
        _eventPollingCts.Dispose();
    }
}
