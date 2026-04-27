using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Auth;
using NexCode.Gui.Infrastructure;
using NexCode.Gui.Services;
using NexCode.Gui.Store;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using NexCode.Shared.Models;
using Windows.Graphics;
using WinRT.Interop;

namespace NexCode.Gui;

public sealed partial class MainWindow : Window
{
    private readonly HelperControlClient _helperControlClient = new();
    private readonly AuthGatePresentationState _authGateState = new();
    private readonly StorePurchaseService _storePurchaseService = new();
    private readonly WindowSizeConstraintHelper _windowSizeConstraintHelper;
    private readonly CancellationTokenSource _eventPollingCts = new();
    private readonly ObservableCollection<string> _runActivities = [];
    private readonly ObservableCollection<string> _sessionTranscript = [];
    private readonly StringBuilder _streamingAssistantBuffer = new();
    private long _lastServiceEventSequence;
    private bool _hasLoadedAccountSnapshot;
    private bool _isAdjustingTierControlledUi;
    private bool _turnInProgress;
    private SubscriptionTier _activeGateRequiredTier = SubscriptionTier.Pro;
    private string _activeGateFeatureName = "This feature";
    private AccountSnapshotPayload? _latestAccountSnapshot;
    private Guid? _activeSessionId;
    private string _activeSessionDescriptor = "No active session";

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Title = "NexCode";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.ResizeClient(new SizeInt32(1440, 900));
        _windowSizeConstraintHelper = WindowSizeConstraintHelper.Attach(this, 800, 600);
        Closed += MainWindow_Closed;
        RunActivityListView.ItemsSource = _runActivities;
        SessionTranscriptListView.ItemsSource = _sessionTranscript;
        AddRunActivityLine("Run activity initialized.");
        UpdateSessionComposerState();
        ApplyAuthGatePresentation();
        _ = RefreshHelperStatusAsync();
        _ = PollServiceEventsAsync(_eventPollingCts.Token);
    }

    private async void PingHelperButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshHelperStatusAsync();
    }

    private async Task RefreshHelperStatusAsync()
    {
        HelperProgressRing.IsActive = true;
        HelperStatusTextBlock.Text = "Checking...";
        SignInButton.IsEnabled = false;
        AuthGateSignInButton.IsEnabled = false;
        RestorePurchasesButton.IsEnabled = false;

        try
        {
            var status = await _helperControlClient.GetHealthAsync();
            var account = await _helperControlClient.GetAccountSnapshotAsync();
            HelperStatusTextBlock.Text = $"{status.State} ({status.ActiveSessions} sessions)";
            RunActivityStatusTextBlock.Text = "Connected to the helper. Listening for auth and session activity.";
            UpdateAccountState(account);
            AccountActionStatusTextBlock.Text = account.Auth.RequiresAuthentication
                ? "Authentication is required before the main dashboard can be considered ready."
                : "Account snapshot refreshed successfully.";
        }
        catch (Exception ex)
        {
            HelperStatusTextBlock.Text = $"Unavailable: {ex.GetType().Name}";
            if (!_hasLoadedAccountSnapshot)
            {
                AccountStatusTextBlock.Text = "Auth: unavailable";
                SubscriptionStatusTextBlock.Text = "Tier: unavailable";
                AccountDetailEmailTextBlock.Text = "Email: unavailable";
                AccountDetailAuthTextBlock.Text = "MSAL configuration: unavailable";
                AccountDetailTokenTextBlock.Text = "Token cache: unavailable";
                AccountDetailTierTextBlock.Text = "Subscription tier: unavailable";
                AccountDetailProductsTextBlock.Text = "Cached products: unavailable";
                AccountDetailWarningTextBlock.Text = "Subscription verification: unavailable";
            }

            AccountActionStatusTextBlock.Text = $"Helper request failed: {ex.Message}";
            RunActivityStatusTextBlock.Text = "Helper connection failed. Event polling will resume automatically when the helper is available again.";
            AddRunActivityLine($"Helper unavailable: {ex.Message}");
            _authGateState.RecordRefreshFailure(ex.Message);
        }
        finally
        {
            HelperProgressRing.IsActive = false;
            UpdateSessionComposerState();
            ApplyAuthGatePresentation();
        }
    }

    private void UpdateAccountState(AccountSnapshotPayload accountSnapshot)
    {
        var auth = accountSnapshot.Auth;
        var subscription = accountSnapshot.Subscription;
        var capabilities = accountSnapshot.Capabilities;
        var emailText = string.IsNullOrWhiteSpace(auth.UserEmail) ? "Auth required" : auth.UserEmail;
        var superUserSuffix = auth.IsSuperUser ? " (SuperUser)" : string.Empty;
        var products = subscription.ProductIds.Length == 0
            ? "none"
            : string.Join(", ", subscription.ProductIds);
        var authSummary = auth.IsAuthenticated
            ? emailText
            : auth.RequiresAuthentication
                ? $"Sign-in required ({emailText})"
                : emailText;

        _hasLoadedAccountSnapshot = true;
        _latestAccountSnapshot = accountSnapshot;
        AccountStatusTextBlock.Text = $"Auth: {authSummary}{superUserSuffix}";
        SubscriptionStatusTextBlock.Text = $"Tier: {subscription.Tier}";

        AccountDetailEmailTextBlock.Text = $"Email: {emailText}";
        AccountDetailAuthTextBlock.Text = auth.HasMsalConfiguration
            ? "MSAL configuration: client/tenant configured"
            : "MSAL configuration: missing client or tenant ID";
        AccountDetailTokenTextBlock.Text = auth.HasCachedToken
            ? $"Token cache: present ({auth.LastAuthenticatedAt?.ToString("u") ?? "timestamp unavailable"})"
            : "Token cache: none";
        AccountDetailTierTextBlock.Text = $"Subscription tier: {subscription.Tier} (expires {subscription.ExpiresAt?.ToString("u") ?? "not scheduled"})";
        AccountDetailProductsTextBlock.Text = $"Cached products: {products} ({subscription.Source}; verified {subscription.VerifiedAt?.ToString("u") ?? "never"})";
        AccountDetailWarningTextBlock.Text = string.IsNullOrWhiteSpace(subscription.Warning)
            ? "Subscription verification: no warnings"
            : $"Subscription verification: {subscription.Warning}";
        UpdateSubscriptionBanner(accountSnapshot);
        SyncTierControlledUi(capabilities);
        _authGateState.ApplySnapshot(accountSnapshot, DateTimeOffset.UtcNow);
        ApplyAuthGatePresentation();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        AccountActionStatusTextBlock.Text = "Starting Microsoft sign-in...";
        _authGateState.StartInteractiveSignIn(DateTimeOffset.UtcNow);
        ApplyAuthGatePresentation();

        try
        {
            var snapshot = await _helperControlClient.SignInAsync();
            UpdateAccountState(snapshot);
            AccountActionStatusTextBlock.Text = snapshot.Auth.IsAuthenticated
                ? "Microsoft sign-in completed successfully."
                : "Sign-in completed, but authentication is still required.";
        }
        catch (Exception ex)
        {
            AccountActionStatusTextBlock.Text = $"Sign-in failed: {ex.Message}";
            _authGateState.RecordSignInFailure(ex.Message, DateTimeOffset.UtcNow);
            ApplyAuthGatePresentation();
        }
        finally
        {
            try
            {
                var snapshot = await _helperControlClient.GetAccountSnapshotAsync();
                UpdateAccountState(snapshot);
            }
            catch
            {
                ApplyAuthGatePresentation();
            }
        }
    }

    private async void RefreshSubscriptionButton_Click(object sender, RoutedEventArgs e)
    {
        AccountActionStatusTextBlock.Text = "Refreshing Microsoft Store subscription state...";
        RestorePurchasesButton.IsEnabled = false;

        try
        {
            var response = await _helperControlClient.RefreshSubscriptionAsync();
            UpdateAccountState(response.Snapshot);
            AccountActionStatusTextBlock.Text = response.StatusMessage
                                               ?? "Microsoft Store subscription refresh completed.";
            RunActivityStatusTextBlock.Text = response.StoreRefreshSucceeded
                ? "Microsoft Store subscription validation completed."
                : "Microsoft Store subscription validation fell back to cached or Free tier state.";
            AddRunActivityLine(response.StoreRefreshSucceeded
                ? "subscription.refresh: windows-store"
                : response.UsedCachedFallback
                    ? "subscription.refresh: cached fallback"
                    : "subscription.refresh: free fallback");
        }
        catch (Exception ex)
        {
            AccountActionStatusTextBlock.Text = $"Subscription refresh failed: {ex.Message}";
            RunActivityStatusTextBlock.Text = "Subscription refresh failed before the helper could return a new snapshot.";
            AddRunActivityLine($"subscription.refresh.failed: {ex.Message}");
        }
        finally
        {
            ApplyAuthGatePresentation();
        }
    }

    private async void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestAccountSnapshot is null)
        {
            await RefreshHelperStatusAsync();
            if (_latestAccountSnapshot is null)
            {
                AccountActionStatusTextBlock.Text = "The helper account snapshot is unavailable, so NexCode could not create a session yet.";
                return;
            }
        }

        var request = new SessionCreateRequest(
            ProjectPath: "C:\\Projects\\NexCode",
            Mode: GetSelectedSessionMode(),
            ExecutionMode: GetSelectedExecutionMode(),
            PermissionLevel: PermissionLevel.Default,
            SandboxEnabled: SandboxToggleSwitch.IsOn);

        var preflight = SubscriptionCapabilityPolicyForGui.Validate(
            request,
            _latestAccountSnapshot.Capabilities,
            activeSessionsHint: null);
        if (!preflight.Allowed)
        {
            ShowSubscriptionGate(preflight.RequiredTier, preflight.FeatureName, preflight.Message);
            return;
        }

        try
        {
            var response = await _helperControlClient.CreateSessionAsync(request);
            ActivateSession(response.SessionId, request);
            AccountActionStatusTextBlock.Text = $"Session {response.SessionId} created successfully.";
            RunActivityStatusTextBlock.Text = "A new helper-backed session was created.";
            AddRunActivityLine($"session.created: {response.SessionId} ({request.Mode}/{request.ExecutionMode}, sandbox={request.SandboxEnabled})");
            await RefreshHelperStatusAsync();
        }
        catch (HelperRequestException ex)
        {
            AccountActionStatusTextBlock.Text = ex.Message;
            RunActivityStatusTextBlock.Text = "The helper rejected the requested session configuration.";
            if (ex.Code == -32021)
            {
                var gate = ResolveGateFromHelperRejection(ex.Message);
                ShowSubscriptionGate(gate.RequiredTier, gate.FeatureName, ex.Message);
                return;
            }

            AddRunActivityLine($"session.create.failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            AccountActionStatusTextBlock.Text = $"Session creation failed: {ex.Message}";
            AddRunActivityLine($"session.create.failed: {ex.Message}");
        }
    }

    private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSessionId is null)
        {
            SessionTurnStatusTextBlock.Text = "Create a session before sending a message.";
            return;
        }

        var content = ComposerTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            SessionTurnStatusTextBlock.Text = "Enter a message before sending it to the helper.";
            return;
        }

        ComposerTextBox.Text = string.Empty;
        AppendSessionTranscriptLine($"You: {content}");
        SessionTurnStatusTextBlock.Text = "Submitting the message to the helper...";
        _turnInProgress = true;
        UpdateSessionComposerState();

        try
        {
            var response = await _helperControlClient.SendMessageAsync(
                new SessionSendMessageRequest(_activeSessionId.Value, content));
            AddRunActivityLine($"session.message.accepted: {response.SessionId}");
            SessionTurnStatusTextBlock.Text = "Message accepted. Waiting for streamed helper events...";
        }
        catch (Exception ex)
        {
            _turnInProgress = false;
            SessionTurnStatusTextBlock.Text = $"The helper rejected the message: {ex.Message}";
            AppendSessionTranscriptLine($"System: message send failed - {ex.Message}");
            UpdateSessionComposerState();
        }
    }

    private void ExecutionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isAdjustingTierControlledUi || _latestAccountSnapshot is null)
        {
            return;
        }

        var mode = GetSelectedExecutionMode();
        if (mode == ExecutionMode.Local)
        {
            return;
        }

        var capabilities = _latestAccountSnapshot.Capabilities;
        if (mode == ExecutionMode.Remote && capabilities.CanUseRemoteExecution)
        {
            return;
        }

        if (mode == ExecutionMode.Cloud && capabilities.CanUseCloudExecution)
        {
            return;
        }

        _isAdjustingTierControlledUi = true;
        ExecutionModeComboBox.SelectedIndex = 0;
        _isAdjustingTierControlledUi = false;

        if (mode == ExecutionMode.Remote)
        {
            ShowSubscriptionGate(SubscriptionTier.Pro, "Remote execution", "Remote execution requires NexCode Pro or higher.");
        }
        else
        {
            ShowSubscriptionGate(SubscriptionTier.Team, "Cloud execution", "Cloud execution requires NexCode Team or higher.");
        }
    }

    private void SandboxToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isAdjustingTierControlledUi || _latestAccountSnapshot is null)
        {
            return;
        }

        if (!SandboxToggleSwitch.IsOn)
        {
            return;
        }

        if (_latestAccountSnapshot.Capabilities.CanUseSandbox)
        {
            return;
        }

        _isAdjustingTierControlledUi = true;
        SandboxToggleSwitch.IsOn = false;
        _isAdjustingTierControlledUi = false;
        ShowSubscriptionGate(SubscriptionTier.Pro, "Sandbox mode", "Sandbox mode requires NexCode Pro or higher.");
    }

    private void ShowSubscriptionGateButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSubscriptionGate(
            requiredTier: _latestAccountSnapshot?.Subscription.Tier == SubscriptionTier.Free
                ? SubscriptionTier.Pro
                : SubscriptionTier.Team,
            featureName: "Paid NexCode features",
            detail: "Compare tiers and restore purchases before trying a paid feature again.");
    }

    private void HideSubscriptionGateButton_Click(object sender, RoutedEventArgs e)
    {
        SubscriptionGateSheet.Visibility = Visibility.Collapsed;
    }

    private async void UpgradeToProButton_Click(object sender, RoutedEventArgs e)
    {
        await StartStorePurchaseAsync("nexcode_pro_monthly");
    }

    private async void UpgradeToTeamButton_Click(object sender, RoutedEventArgs e)
    {
        await StartStorePurchaseAsync("nexcode_team_monthly");
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
                RunActivityStatusTextBlock.Text = "Event polling hit an error. The next tick will retry automatically.";
            }
        }
    }

    private void ProcessServiceEvent(ServiceEventEnvelope serviceEvent)
    {
        switch (serviceEvent.EventType)
        {
            case ServiceEventTypes.AuthRequired:
            {
                var payload = serviceEvent.Payload.Deserialize<AuthRequiredEventPayload>(JsonSerialization.Options);
                if (payload is null)
                {
                    return;
                }

                AccountActionStatusTextBlock.Text = payload.HasMsalConfiguration
                    ? "The helper requested Microsoft sign-in."
                    : "The helper is missing Microsoft authentication configuration.";
                _authGateState.ApplyAuthRequiredEvent(payload, serviceEvent.Timestamp);
                ApplyAuthGatePresentation();
                RunActivityStatusTextBlock.Text = "Auth gate is active.";
                AddRunActivityLine($"auth.required: {payload.Reason}");
                break;
            }
            case ServiceEventTypes.AuthSuccess:
            {
                var payload = serviceEvent.Payload.Deserialize<AuthSuccessEventPayload>(JsonSerialization.Options);
                AccountActionStatusTextBlock.Text = payload?.IsSuperUser == true
                    ? "Microsoft sign-in completed. Sealed superuser grant recognized."
                    : "Microsoft sign-in completed successfully.";
                _authGateState.ApplyAuthSuccessEvent(payload, serviceEvent.Timestamp);
                ApplyAuthGatePresentation();
                RunActivityStatusTextBlock.Text = "Authentication succeeded.";
                AddRunActivityLine(payload?.IsSuperUser == true
                    ? "auth.success: sealed superuser grant recognized"
                    : $"auth.success: {payload?.UserEmail ?? "authenticated"}");
                _ = RefreshHelperStatusAsync();
                break;
            }
            case ServiceEventTypes.SessionLifecycle:
            {
                var payload = serviceEvent.Payload.Deserialize<SessionLifecycleEventPayload>(JsonSerialization.Options);
                if (payload is null)
                {
                    return;
                }

                RunActivityStatusTextBlock.Text = "Session lifecycle activity received from the helper.";
                var summary = payload.State.Equals("created", StringComparison.OrdinalIgnoreCase)
                    ? $"session.created: {payload.SessionId} ({payload.Mode}/{payload.ExecutionMode})"
                    : $"session.cancelled: {payload.SessionId}";
                AddRunActivityLine(summary);
                if (_activeSessionId == payload.SessionId && payload.State.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    SessionTurnStatusTextBlock.Text = "The active session was cancelled.";
                    AppendSessionTranscriptLine("System: the active session was cancelled.");
                    _turnInProgress = false;
                    UpdateSessionComposerState();
                }
                break;
            }
            case ServiceEventTypes.SessionStart:
            {
                var payload = serviceEvent.Payload.Deserialize<SessionStartEventPayload>(JsonSerialization.Options);
                if (payload is null || _activeSessionId != payload.SessionId)
                {
                    return;
                }

                _turnInProgress = true;
                _streamingAssistantBuffer.Clear();
                StreamingAssistantTextBlock.Text = string.Empty;
                StreamingAssistantBorder.Visibility = Visibility.Visible;
                SessionTurnStatusTextBlock.Text = "The helper started streaming a turn.";
                UpdateSessionComposerState();
                AddRunActivityLine($"session.start: {payload.SessionId}");
                break;
            }
            case ServiceEventTypes.Status:
            {
                var payload = serviceEvent.Payload.Deserialize<StatusEventPayload>(JsonSerialization.Options);
                if (payload is null)
                {
                    return;
                }

                AddRunActivityLine($"status.{payload.Level}: {payload.Message}");
                if (_activeSessionId == payload.SessionId)
                {
                    SessionTurnStatusTextBlock.Text = payload.Message;
                }
                break;
            }
            case ServiceEventTypes.Token:
            {
                var payload = serviceEvent.Payload.Deserialize<TokenEventPayload>(JsonSerialization.Options);
                if (payload is null || _activeSessionId != payload.SessionId)
                {
                    return;
                }

                _streamingAssistantBuffer.Append(payload.Content);
                StreamingAssistantTextBlock.Text = _streamingAssistantBuffer.ToString();
                StreamingAssistantBorder.Visibility = Visibility.Visible;
                break;
            }
            case ServiceEventTypes.Checkpoint:
            {
                var payload = serviceEvent.Payload.Deserialize<CheckpointEventPayload>(JsonSerialization.Options);
                if (payload is null)
                {
                    return;
                }

                AddRunActivityLine($"checkpoint: {payload.CheckpointId}");
                if (_activeSessionId == payload.SessionId)
                {
                    LatestCheckpointBorder.Visibility = Visibility.Visible;
                    LatestCheckpointSummaryTextBlock.Text = payload.DiffSummary;
                    LatestCheckpointFilesTextBlock.Text = payload.FilesChanged.Length == 0
                        ? "No changed files were recorded for this foundation checkpoint."
                        : string.Join(Environment.NewLine, payload.FilesChanged.Select(path => $"• {path}"));
                }
                break;
            }
            case ServiceEventTypes.SessionEnd:
            {
                var payload = serviceEvent.Payload.Deserialize<SessionEndEventPayload>(JsonSerialization.Options);
                if (payload is null || _activeSessionId != payload.SessionId)
                {
                    return;
                }

                if (_streamingAssistantBuffer.Length > 0)
                {
                    AppendSessionTranscriptLine($"NexCode: {_streamingAssistantBuffer}");
                    _streamingAssistantBuffer.Clear();
                    StreamingAssistantTextBlock.Text = string.Empty;
                    StreamingAssistantBorder.Visibility = Visibility.Collapsed;
                }

                _turnInProgress = false;
                SessionTurnStatusTextBlock.Text = payload.Reason.Equals("completed", StringComparison.OrdinalIgnoreCase)
                    ? "The helper completed the active turn."
                    : $"The active turn ended with status: {payload.Reason}";
                UpdateSessionComposerState();
                AddRunActivityLine($"session.end: {payload.Reason}");
                break;
            }
        }
    }

    private void ApplyAuthGatePresentation()
    {
        var presentation = _authGateState.Build();

        SignInButton.IsEnabled = presentation.IsSignInEnabled;
        AuthGateSignInButton.IsEnabled = presentation.IsSignInEnabled;
        RestorePurchasesButton.IsEnabled = _hasLoadedAccountSnapshot;
        SignInButton.Content = presentation.SignInButtonLabel;
        AuthGateSignInButton.Content = presentation.SignInButtonLabel;
        AuthGateTitleTextBlock.Text = presentation.Title;
        AuthGateBodyTextBlock.Text = presentation.Body;
        AuthGateStatusTextBlock.Text = presentation.PrimaryStatus;
        AuthGatePollStatusTextBlock.Text = presentation.EventStreamStatus;
        AuthGateEventDetailTextBlock.Text = presentation.EventStreamDetail;
        AuthGateEventStreamProgressRing.IsActive = presentation.IsEventStreamActive;
        AuthGateOverlay.Visibility = presentation.IsOverlayVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSessionComposerState();
    }

    private void UpdateSubscriptionBanner(AccountSnapshotPayload snapshot)
    {
        var subscription = snapshot.Subscription;
        var capabilities = snapshot.Capabilities;
        var shouldShow = subscription.Tier == SubscriptionTier.Free || !string.IsNullOrWhiteSpace(subscription.Warning);

        SubscriptionBannerBorder.Visibility = shouldShow
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!shouldShow)
        {
            return;
        }

        SubscriptionBannerTitleTextBlock.Text = subscription.Tier == SubscriptionTier.Free
            ? "Free tier limits are active"
            : "Subscription verification needs attention";
        SubscriptionBannerBodyTextBlock.Text = !string.IsNullOrWhiteSpace(subscription.Warning)
            ? subscription.Warning
            : $"Your current tier is {subscription.Tier}. This shell currently allows {capabilities.MaxConcurrentSessions} concurrent session(s), sandbox: {(capabilities.CanUseSandbox ? "yes" : "no")}, remote: {(capabilities.CanUseRemoteExecution ? "yes" : "no")}, cloud: {(capabilities.CanUseCloudExecution ? "yes" : "no")}.";
    }

    private void SyncTierControlledUi(SubscriptionCapabilitiesPayload capabilities)
    {
        _isAdjustingTierControlledUi = true;

        if (!capabilities.CanUseSandbox && SandboxToggleSwitch.IsOn)
        {
            SandboxToggleSwitch.IsOn = false;
        }

        var selectedMode = GetSelectedExecutionMode();
        if (selectedMode == ExecutionMode.Remote && !capabilities.CanUseRemoteExecution)
        {
            ExecutionModeComboBox.SelectedIndex = 0;
        }
        else if (selectedMode == ExecutionMode.Cloud && !capabilities.CanUseCloudExecution)
        {
            ExecutionModeComboBox.SelectedIndex = 0;
        }

        _isAdjustingTierControlledUi = false;
    }

    private void ShowSubscriptionGate(SubscriptionTier requiredTier, string featureName, string detail)
    {
        _activeGateRequiredTier = requiredTier;
        _activeGateFeatureName = featureName;
        SubscriptionGateTitleTextBlock.Text = $"{featureName} requires {requiredTier}";
        SubscriptionGateBodyTextBlock.Text = detail;
        SubscriptionGateStatusTextBlock.Text = "Use Restore purchases if you already own a subscription, or start a Microsoft Store purchase from one of the upgrade buttons.";
        UpgradeToProButton.Visibility = requiredTier == SubscriptionTier.Team
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpgradeToTeamButton.Visibility = Visibility.Visible;
        SubscriptionGateSheet.Visibility = Visibility.Visible;
        AddRunActivityLine($"subscription.gate: {featureName} requires {requiredTier}");
    }

    private async Task StartStorePurchaseAsync(string productId)
    {
        SubscriptionGateStatusTextBlock.Text = $"Opening Microsoft Store purchase flow for {productId}...";

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var result = await _storePurchaseService.RequestPurchaseAsync(productId, hwnd);
            SubscriptionGateStatusTextBlock.Text = result.Message;
            if (result.ShouldRefreshSubscription)
            {
                await RefreshSubscriptionAfterPurchaseAttemptAsync();
            }
        }
        catch (Exception ex)
        {
            SubscriptionGateStatusTextBlock.Text = $"Microsoft Store purchase failed: {ex.Message}";
        }
    }

    private async Task RefreshSubscriptionAfterPurchaseAttemptAsync()
    {
        try
        {
            var refreshResponse = await _helperControlClient.RefreshSubscriptionAsync();
            UpdateAccountState(refreshResponse.Snapshot);
            AccountActionStatusTextBlock.Text = refreshResponse.StatusMessage ?? "Subscription refresh completed.";
        }
        catch (Exception ex)
        {
            AccountActionStatusTextBlock.Text = $"Subscription refresh after purchase attempt failed: {ex.Message}";
        }
    }

    private SessionMode GetSelectedSessionMode()
    {
        return ModeComboBox.SelectedIndex switch
        {
            1 => SessionMode.Plan,
            2 => SessionMode.Debug,
            3 => SessionMode.Ask,
            _ => SessionMode.Code
        };
    }

    private ExecutionMode GetSelectedExecutionMode()
    {
        return ExecutionModeComboBox.SelectedIndex switch
        {
            1 => ExecutionMode.Remote,
            2 => ExecutionMode.Cloud,
            _ => ExecutionMode.Local
        };
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _eventPollingCts.Cancel();
        _eventPollingCts.Dispose();
    }

    private void AddRunActivityLine(string text)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss}  {text}";
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _runActivities.Insert(0, line);
                while (_runActivities.Count > 50)
                {
                    _runActivities.RemoveAt(_runActivities.Count - 1);
                }
            }))
        {
            _runActivities.Insert(0, line);
        }
    }

    private void ActivateSession(Guid sessionId, SessionCreateRequest request)
    {
        _activeSessionId = sessionId;
        _activeSessionDescriptor = $"{request.Mode} / {request.ExecutionMode} / sandbox {(request.SandboxEnabled ? "on" : "off")}";
        _turnInProgress = false;
        _sessionTranscript.Clear();
        _streamingAssistantBuffer.Clear();
        StreamingAssistantTextBlock.Text = string.Empty;
        StreamingAssistantBorder.Visibility = Visibility.Collapsed;
        LatestCheckpointBorder.Visibility = Visibility.Collapsed;
        LatestCheckpointSummaryTextBlock.Text = "Checkpoint details will appear here.";
        LatestCheckpointFilesTextBlock.Text = "No changed files recorded yet.";
        ActiveSessionSummaryTextBlock.Text = $"Active session {_activeSessionId} · {_activeSessionDescriptor}";
        SessionTurnStatusTextBlock.Text = "Session created. Send a message to begin the first helper-backed turn.";
        UpdateSessionComposerState();
    }

    private void UpdateSessionComposerState()
    {
        var authGateVisible = AuthGateOverlay.Visibility == Visibility.Visible;
        var canCompose = _activeSessionId is not null && !_turnInProgress && !authGateVisible;

        ComposerTextBox.IsEnabled = canCompose;
        SendMessageButton.IsEnabled = canCompose;
        StopTurnButton.IsEnabled = false;

        if (_activeSessionId is null)
        {
            ActiveSessionSummaryTextBlock.Text = "Create a session to start a helper-backed turn.";
            if (!_turnInProgress)
            {
                SessionTurnStatusTextBlock.Text = "No active session is selected.";
            }
        }
    }

    private void AppendSessionTranscriptLine(string text)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss}  {text}";
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _sessionTranscript.Insert(0, line);
                while (_sessionTranscript.Count > 80)
                {
                    _sessionTranscript.RemoveAt(_sessionTranscript.Count - 1);
                }
            }))
        {
            _sessionTranscript.Insert(0, line);
        }
    }

    private (SubscriptionTier RequiredTier, string FeatureName) ResolveGateFromHelperRejection(string message)
    {
        if (message.Contains("Cloud execution", StringComparison.OrdinalIgnoreCase))
        {
            return (SubscriptionTier.Team, "Cloud execution");
        }

        if (message.Contains("Remote execution", StringComparison.OrdinalIgnoreCase))
        {
            return (SubscriptionTier.Pro, "Remote execution");
        }

        if (message.Contains("Sandbox mode", StringComparison.OrdinalIgnoreCase))
        {
            return (SubscriptionTier.Pro, "Sandbox mode");
        }

        if (message.Contains("concurrent session", StringComparison.OrdinalIgnoreCase))
        {
            var requiredTier = _latestAccountSnapshot?.Capabilities.MaxConcurrentSessions switch
            {
                1 => SubscriptionTier.Pro,
                5 => SubscriptionTier.Team,
                20 => SubscriptionTier.Enterprise,
                _ => SubscriptionTier.Enterprise
            };

            return (requiredTier, "Concurrent sessions");
        }

        return (SubscriptionTier.Pro, "Paid NexCode feature");
    }

    private static class SubscriptionCapabilityPolicyForGui
    {
        public static (bool Allowed, SubscriptionTier RequiredTier, string FeatureName, string Message) Validate(
            SessionCreateRequest request,
            SubscriptionCapabilitiesPayload capabilities,
            int? activeSessionsHint)
        {
            if (request.SandboxEnabled && !capabilities.CanUseSandbox)
            {
                return (false, SubscriptionTier.Pro, "Sandbox mode", "Sandbox mode requires NexCode Pro or higher.");
            }

            if (request.ExecutionMode == ExecutionMode.Remote && !capabilities.CanUseRemoteExecution)
            {
                return (false, SubscriptionTier.Pro, "Remote execution", "Remote execution requires NexCode Pro or higher.");
            }

            if (request.ExecutionMode == ExecutionMode.Cloud && !capabilities.CanUseCloudExecution)
            {
                return (false, SubscriptionTier.Team, "Cloud execution", "Cloud execution requires NexCode Team or higher.");
            }

            if (activeSessionsHint is not null && activeSessionsHint >= capabilities.MaxConcurrentSessions)
            {
                return capabilities.MaxConcurrentSessions switch
                {
                    1 => (false, SubscriptionTier.Pro, "Concurrent sessions", "Free tier allows only 1 concurrent session."),
                    5 => (false, SubscriptionTier.Team, "Concurrent sessions", "Pro tier allows up to 5 concurrent sessions."),
                    20 => (false, SubscriptionTier.Enterprise, "Concurrent sessions", "Team tier allows up to 20 concurrent sessions."),
                    _ => (false, SubscriptionTier.Enterprise, "Concurrent sessions", "Your current tier has reached its concurrent session limit.")
                };
            }

            return (true, SubscriptionTier.Free, string.Empty, string.Empty);
        }
    }
}
