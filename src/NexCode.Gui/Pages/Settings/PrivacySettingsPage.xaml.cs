using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Privacy / telemetry / GDPR panel (spec §31).</summary>
public sealed partial class PrivacySettingsPage : Page
{
    public PrivacySettingsViewModel ViewModel { get; } = new();

    public PrivacySettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
