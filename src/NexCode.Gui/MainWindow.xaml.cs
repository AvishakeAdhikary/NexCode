using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NexCode.Gui.Auth;
using NexCode.Gui.Infrastructure;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using Windows.Graphics;

namespace NexCode.Gui;

public sealed partial class MainWindow : Window
{
    private readonly HelperControlClient _helperControlClient = new();
    private readonly AuthGatePresentationState _authGateState = new();
    private readonly WindowSizeConstraintHelper _windowSizeConstraintHelper;
    private readonly CancellationTokenSource _eventPollingCts = new();
    private readonly ObservableCollection<string> _runActivities = [];
    private long _lastServiceEventSequence;
    private bool _hasLoadedAccountSnapshot;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Title = "NexCode";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.ResizeClient(new SizeInt32(1440, 900));
        _windowSizeConstraintHelper = WindowSizeConstraintHelper.Attach(this, 800, 600);
        Closed += MainWindow_Closed;
        RunActivityListView.ItemsSource = _runActivities;
        AddRunActivityLine("Run activity initialized.");
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
            ApplyAuthGatePresentation();
        }
    }

    private void UpdateAccountState(AccountSnapshotPayload accountSnapshot)
    {
        var auth = accountSnapshot.Auth;
        var subscription = accountSnapshot.Subscription;
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
}
