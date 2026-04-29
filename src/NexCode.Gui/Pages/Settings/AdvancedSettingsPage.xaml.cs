using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Advanced diagnostics panel (spec §32). Reset-to-defaults uses a two-step
/// <see cref="ContentDialog"/> confirmation so a single misclick does not nuke preferences.
/// </summary>
public sealed partial class AdvancedSettingsPage : Page
{
    public AdvancedSettingsPage()
    {
        InitializeComponent();
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var first = new ContentDialog
        {
            Title = "Reset preferences?",
            Content = "This will clear all preferences except your subscription state. " +
                      "Open sessions and projects are unaffected.",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        if (await first.ShowAsync() != ContentDialogResult.Primary)
        {
            StatusTextBlock.Text = "Reset cancelled.";
            return;
        }

        var second = new ContentDialog
        {
            Title = "Are you sure?",
            Content = "This action is not reversible. Type-confirmation will land in slice 0017.",
            PrimaryButtonText = "Reset now",
            CloseButtonText = "Back out",
            XamlRoot = XamlRoot,
        };

        StatusTextBlock.Text = await second.ShowAsync() == ContentDialogResult.Primary
            ? "Preferences reset (wire-up pending)."
            : "Reset cancelled at the second step.";
    }
}
