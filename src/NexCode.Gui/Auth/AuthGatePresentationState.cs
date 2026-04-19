using NexCode.Shared.Contracts;

namespace NexCode.Gui.Auth;

internal readonly record struct AuthGatePresentation(
    bool IsOverlayVisible,
    string Title,
    string Body,
    string PrimaryStatus,
    string EventStreamStatus,
    string EventStreamDetail,
    bool IsEventStreamActive,
    bool IsSignInEnabled,
    string SignInButtonLabel);

internal sealed class AuthGatePresentationState
{
    private AuthGateMode _mode;
    private bool _hasKnownAuthState;
    private bool _isAuthenticated;
    private bool _hasMsalConfiguration;
    private bool _hasCachedToken;
    private bool _isSignInInProgress;
    private string? _lastHelperError;
    private string? _lastPollError;
    private string? _statusOverride;
    private string? _lastEventDescription;
    private DateTimeOffset? _lastSnapshotAt;
    private DateTimeOffset? _lastPollAt;
    private DateTimeOffset? _lastEventAt;
    private string _signInButtonLabel = "Sign in with Microsoft";

    public void ApplySnapshot(AccountSnapshotPayload snapshot, DateTimeOffset observedAt)
    {
        var auth = snapshot.Auth;

        _hasKnownAuthState = true;
        _isAuthenticated = auth.IsAuthenticated;
        _hasMsalConfiguration = auth.HasMsalConfiguration;
        _hasCachedToken = auth.HasCachedToken;
        _isSignInInProgress = false;
        _lastHelperError = null;
        _lastSnapshotAt = observedAt;
        _signInButtonLabel = auth.IsAuthenticated
            ? "Refresh sign-in"
            : "Sign in with Microsoft";

        _mode = auth.IsAuthenticated
            ? AuthGateMode.Hidden
            : auth.HasMsalConfiguration
                ? AuthGateMode.MicrosoftSignInRequired
                : AuthGateMode.AuthenticationSetupRequired;

        _statusOverride = auth.IsAuthenticated
            ? null
            : auth.HasMsalConfiguration
                ? auth.HasCachedToken
                    ? "A cached token exists, but the helper still needs interactive sign-in."
                    : "No valid token is available yet. Complete Microsoft sign-in to continue."
                : "Microsoft sign-in stays disabled until the helper has both the client ID and tenant ID.";
    }

    public void RecordRefreshFailure(string message)
    {
        _lastHelperError = message;
        _isSignInInProgress = false;
        _statusOverride = null;

        if (_isAuthenticated)
        {
            return;
        }

        _mode = AuthGateMode.HelperUnavailable;
    }

    public void RecordPollSuccess(DateTimeOffset observedAt)
    {
        _lastPollAt = observedAt;
        _lastPollError = null;

        if (_mode == AuthGateMode.HelperUnavailable)
        {
            _mode = ResolveRecoveredModeAfterHelperReconnect();
            _statusOverride = _mode == AuthGateMode.WaitingForState
                ? "The helper event stream reconnected. Waiting for the latest account snapshot."
                : _statusOverride;
        }
    }

    public void RecordPollFailure(string message)
    {
        _lastPollError = message;
    }

    public void StartInteractiveSignIn(DateTimeOffset observedAt)
    {
        _isSignInInProgress = true;
        _statusOverride = "Waiting for the helper to confirm Microsoft sign-in...";
        _lastEventAt = observedAt;
        _lastEventDescription = "Interactive sign-in launched from the GUI.";
    }

    public void RecordSignInFailure(string message, DateTimeOffset observedAt)
    {
        _isSignInInProgress = false;
        _statusOverride = $"Sign-in needs attention: {message}";
        _lastEventAt = observedAt;
        _lastEventDescription = "Interactive sign-in did not complete successfully.";
    }

    public void ApplyAuthRequiredEvent(AuthRequiredEventPayload payload, DateTimeOffset observedAt)
    {
        _hasKnownAuthState = true;
        _isAuthenticated = false;
        _hasMsalConfiguration = payload.HasMsalConfiguration;
        _hasCachedToken = payload.HasCachedToken;
        _isSignInInProgress = false;
        _lastHelperError = null;
        _mode = payload.HasMsalConfiguration
            ? AuthGateMode.MicrosoftSignInRequired
            : AuthGateMode.AuthenticationSetupRequired;
        _statusOverride = payload.HasMsalConfiguration
            ? "The helper requested Microsoft sign-in before the dashboard can unlock."
            : "The helper reported missing Microsoft authentication configuration.";
        _lastEventAt = observedAt;
        _lastEventDescription = DescribeAuthRequiredReason(payload);
    }

    public void ApplyAuthSuccessEvent(AuthSuccessEventPayload? payload, DateTimeOffset observedAt)
    {
        _hasKnownAuthState = true;
        _isAuthenticated = true;
        _isSignInInProgress = false;
        _hasMsalConfiguration = true;
        _lastHelperError = null;
        _mode = AuthGateMode.Hidden;
        _statusOverride = null;
        _signInButtonLabel = "Refresh sign-in";
        _lastEventAt = observedAt;
        _lastEventDescription = payload?.IsSuperUser == true
            ? "Microsoft sign-in completed. Sealed superuser grant recognized."
            : string.IsNullOrWhiteSpace(payload?.UserEmail)
                ? "Microsoft sign-in completed successfully."
                : $"Microsoft sign-in completed for {payload.UserEmail}.";
    }

    public AuthGatePresentation Build()
    {
        var mode = ResolveVisibleMode();
        var isOverlayVisible = mode != AuthGateMode.Hidden;
        var (title, body) = GetFrameCopy(mode);

        return new AuthGatePresentation(
            IsOverlayVisible: isOverlayVisible,
            Title: title,
            Body: body,
            PrimaryStatus: BuildPrimaryStatus(mode),
            EventStreamStatus: BuildEventStreamStatus(),
            EventStreamDetail: BuildEventStreamDetail(),
            IsEventStreamActive: _lastPollError is null,
            IsSignInEnabled: !_isSignInInProgress && (_isAuthenticated || (_hasMsalConfiguration && mode != AuthGateMode.HelperUnavailable)),
            SignInButtonLabel: _signInButtonLabel);
    }

    private AuthGateMode ResolveVisibleMode()
    {
        if (_isAuthenticated)
        {
            return AuthGateMode.Hidden;
        }

        if (_mode == AuthGateMode.HelperUnavailable)
        {
            return AuthGateMode.HelperUnavailable;
        }

        if (!_hasKnownAuthState)
        {
            return _lastHelperError is null
                ? AuthGateMode.Hidden
                : AuthGateMode.WaitingForState;
        }

        return _hasMsalConfiguration
            ? AuthGateMode.MicrosoftSignInRequired
            : AuthGateMode.AuthenticationSetupRequired;
    }

    private AuthGateMode ResolveRecoveredModeAfterHelperReconnect()
    {
        if (_isAuthenticated)
        {
            return AuthGateMode.Hidden;
        }

        if (!_hasKnownAuthState)
        {
            return AuthGateMode.WaitingForState;
        }

        return _hasMsalConfiguration
            ? AuthGateMode.MicrosoftSignInRequired
            : AuthGateMode.AuthenticationSetupRequired;
    }

    private static string DescribeAuthRequiredReason(AuthRequiredEventPayload payload)
    {
        return payload.Reason switch
        {
            "msal_configuration_missing" => "The helper is missing the AAD client or tenant ID.",
            "silent_refresh_failed" => "Silent token refresh failed, so interactive sign-in is required.",
            "interactive_sign_in_required" => "Interactive Microsoft sign-in is required.",
            _ => payload.HasMsalConfiguration
                ? "The helper requires Microsoft sign-in before the dashboard can unlock."
                : "The helper cannot start Microsoft sign-in until configuration is completed."
        };
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("HH:mm:ss 'UTC'");
    }

    private (string Title, string Body) GetFrameCopy(AuthGateMode mode)
    {
        return mode switch
        {
            AuthGateMode.MicrosoftSignInRequired => (
                "Microsoft sign-in required",
                _hasCachedToken
                    ? "A cached token exists, but the helper still needs an interactive Microsoft sign-in before the dashboard can unlock."
                    : "Complete Microsoft sign-in with the helper to unlock the main dashboard."),
            AuthGateMode.AuthenticationSetupRequired => (
                "Authentication setup required",
                "This build requires Microsoft authentication, but the helper is still missing the Azure AD client or tenant ID."),
            AuthGateMode.HelperUnavailable => (
                "Helper unavailable",
                "The authentication gate cannot be resolved until the background helper responds again."),
            AuthGateMode.WaitingForState => (
                "Checking account access",
                "The helper event stream is connected again. NexCode is waiting for the latest account snapshot before unlocking the dashboard."),
            _ => (
                "Microsoft sign-in required",
                "Complete Microsoft sign-in with the helper to unlock the main dashboard.")
        };
    }

    private string BuildPrimaryStatus(AuthGateMode mode)
    {
        if (mode == AuthGateMode.HelperUnavailable && !string.IsNullOrWhiteSpace(_lastHelperError))
        {
            return $"Last helper error: {_lastHelperError}";
        }

        if (_isSignInInProgress)
        {
            return "Waiting for the helper to confirm Microsoft sign-in...";
        }

        if (!string.IsNullOrWhiteSpace(_statusOverride))
        {
            return _statusOverride;
        }

        if (_lastSnapshotAt is not null)
        {
            return $"Latest account snapshot received at {FormatTimestamp(_lastSnapshotAt.Value)}.";
        }

        if (_lastEventAt is not null && !string.IsNullOrWhiteSpace(_lastEventDescription))
        {
            return $"Latest helper event at {FormatTimestamp(_lastEventAt.Value)}.";
        }

        return mode == AuthGateMode.WaitingForState
            ? "Waiting for the helper to deliver the first account snapshot..."
            : "Waiting for helper state...";
    }

    private string BuildEventStreamStatus()
    {
        if (!string.IsNullOrWhiteSpace(_lastPollError))
        {
            return _lastPollAt is not null
                ? $"Polling paused after the last successful update at {FormatTimestamp(_lastPollAt.Value)}."
                : $"Unable to reach the helper event stream: {_lastPollError}";
        }

        return _lastPollAt is not null
            ? "Listening for helper updates."
            : "Connecting to the helper event stream...";
    }

    private string BuildEventStreamDetail()
    {
        if (!string.IsNullOrWhiteSpace(_lastPollError))
        {
            return $"Last polling error: {_lastPollError}";
        }

        if (_lastEventAt is not null && !string.IsNullOrWhiteSpace(_lastEventDescription))
        {
            return $"Last helper event at {FormatTimestamp(_lastEventAt.Value)}: {_lastEventDescription}";
        }

        if (_lastPollAt is not null)
        {
            return $"Last successful poll at {FormatTimestamp(_lastPollAt.Value)}.";
        }

        return "The helper will push authentication changes here as they happen.";
    }

    private enum AuthGateMode
    {
        Hidden,
        WaitingForState,
        MicrosoftSignInRequired,
        AuthenticationSetupRequired,
        HelperUnavailable
    }
}
