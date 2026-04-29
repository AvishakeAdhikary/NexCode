using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Automations registry panel (spec §32 — slice 0017 wire-up pending).</summary>
public sealed partial class AutomationsSettingsPage : Page
{
    public AutomationsSettingsViewModel ViewModel { get; } = new();

    public AutomationsSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
