using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>Nav-rail Automations destination (spec §22.2). Reuses <see cref="AutomationsSettingsViewModel"/>.</summary>
public sealed partial class AutomationsPage : Page
{
    public AutomationsSettingsViewModel ViewModel { get; } = new();

    public AutomationsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var client = ((App)Application.Current).Services.GetRequiredService<HelperControlClient>();
        await ViewModel.InitializeAsync(client);
    }

    private async void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true } toggle && toggle.DataContext is AutomationRowViewModel row)
        {
            await ViewModel.SetEnabledAsync(row, toggle.IsOn);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAutomation is { } row)
        {
            await ViewModel.SaveAsync(row);
        }
    }
}
