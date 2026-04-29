using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Memory injection/eviction tuning (spec §9).</summary>
public sealed partial class MemorySettingsPage : Page
{
    public MemorySettingsViewModel ViewModel { get; } = new();

    public MemorySettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
