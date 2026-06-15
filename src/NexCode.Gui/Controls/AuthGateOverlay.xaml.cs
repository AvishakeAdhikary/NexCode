using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Auth;

namespace NexCode.Gui.Controls;

public sealed partial class AuthGateOverlay : UserControl
{
    public event EventHandler? SignInRequested;
    public event EventHandler? RefreshRequested;

    private bool _wasVisible;

    public AuthGateOverlay()
    {
        InitializeComponent();
        Loaded += AuthGateOverlay_Loaded;
    }

    private void AuthGateOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
            FadeInStoryboard.Begin();
        }
    }

    /// <summary>
    /// Apply a presentation snapshot built by <see cref="AuthGatePresentationState"/>.
    /// Marked <c>internal</c> because <see cref="AuthGatePresentation"/> is internal.
    /// </summary>
    internal void ApplyPresentation(AuthGatePresentation presentation)
    {
        TitleText.Text = presentation.Title;
        BodyText.Text = presentation.Body;
        StatusText.Text = presentation.PrimaryStatus;
        PollStatusText.Text = presentation.EventStreamStatus;
        EventDetailText.Text = presentation.EventStreamDetail;
        EventStreamProgress.IsActive = presentation.IsEventStreamActive;
        SignInButton.IsEnabled = presentation.IsSignInEnabled;
        SignInButton.Content = presentation.SignInButtonLabel;
        Visibility = presentation.IsOverlayVisible ? Visibility.Visible : Visibility.Collapsed;

        // Only play the fade-in on the hidden -> visible transition. The auth gate
        // presentation is re-applied on every 2-second event poll; restarting the
        // storyboard each time made the overlay pulse/flicker while it stayed open.
        if (presentation.IsOverlayVisible && !_wasVisible)
        {
            FadeInStoryboard.Begin();
        }

        _wasVisible = presentation.IsOverlayVisible;
    }

    private void SignIn_Click(object sender, RoutedEventArgs e) => SignInRequested?.Invoke(this, EventArgs.Empty);
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
