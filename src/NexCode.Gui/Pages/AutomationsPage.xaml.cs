using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>Nav-rail Automations destination (spec §32 — slice 0017 wire-up pending).</summary>
public sealed partial class AutomationsPage : Page
{
    public AutomationsSettingsViewModel ViewModel { get; } = new();

    public AutomationsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
