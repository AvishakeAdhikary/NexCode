using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Plugin registry panel (spec §32 — slice 0017 wire-up pending).</summary>
public sealed partial class PluginsSettingsPage : Page
{
    public PluginsSettingsViewModel ViewModel { get; } = new();

    public PluginsSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
