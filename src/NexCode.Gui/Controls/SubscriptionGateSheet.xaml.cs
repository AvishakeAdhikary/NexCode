using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Shared.Models;

namespace NexCode.Gui.Controls;

public sealed partial class SubscriptionGateSheet : UserControl
{
    public event EventHandler? UpgradeProRequested;
    public event EventHandler? UpgradeTeamRequested;
    public event EventHandler? RestoreRequested;
    public event EventHandler? DismissRequested;

    public SubscriptionGateSheet()
    {
        InitializeComponent();
        Loaded += SubscriptionGateSheet_Loaded;
    }

    private void SubscriptionGateSheet_Loaded(object sender, RoutedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
            SlideUpStoryboard.Begin();
        }
    }

    public void Configure(SubscriptionTier requiredTier, string featureName, string detail, string? statusMessage)
    {
        TitleText.Text = $"{featureName} requires {requiredTier}";
        BodyText.Text = detail;
        StatusText.Text = statusMessage ?? "Use Restore purchases if you already own a subscription, or upgrade below.";
        UpgradeProButton.Visibility = requiredTier == SubscriptionTier.Team ? Visibility.Collapsed : Visibility.Visible;
        UpgradeTeamButton.Visibility = Visibility.Visible;
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        SlideUpStoryboard.Begin();
    }

    private void UpgradePro_Click(object sender, RoutedEventArgs e) => UpgradeProRequested?.Invoke(this, EventArgs.Empty);
    private void UpgradeTeam_Click(object sender, RoutedEventArgs e) => UpgradeTeamRequested?.Invoke(this, EventArgs.Empty);
    private void Restore_Click(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    private void Dismiss_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
