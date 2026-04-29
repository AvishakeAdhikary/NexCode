using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Theme / accent / density / font-scale panel (spec §32).</summary>
public sealed partial class AppearanceSettingsPage : Page
{
    public AppearanceViewModel ViewModel { get; } = new();

    public AppearanceSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            ViewModel.SelectedAccent = hex;
        }
    }
}
