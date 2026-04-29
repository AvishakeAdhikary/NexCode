using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Per-project environment registry panel (spec §32).</summary>
public sealed partial class EnvironmentsSettingsPage : Page
{
    public EnvironmentsSettingsViewModel ViewModel { get; } = new();

    public EnvironmentsSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
