using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace NexCode.Gui.Pages.Settings;

/// <summary>About / legal links panel (spec §32). Links open in the default browser.</summary>
public sealed partial class AboutSettingsPage : Page
{
    public AboutSettingsPage()
    {
        InitializeComponent();
    }

    private async void OpenPrivacy_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://nexcode.app/privacy"));

    private async void OpenTos_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://nexcode.app/terms"));

    private async void OpenEula_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://nexcode.app/eula"));

    private async void OpenLicenses_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://nexcode.app/licenses"));

    private async void OpenChangelog_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://nexcode.app/changelog"));
}
