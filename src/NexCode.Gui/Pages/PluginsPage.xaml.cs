using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>Nav-rail Plugins destination (spec §32). Reuses <see cref="PluginsSettingsViewModel"/>.</summary>
public sealed partial class PluginsPage : Page
{
    public PluginsSettingsViewModel ViewModel { get; } = new();

    public PluginsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
