using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Automations registry panel (spec §22.2). Backed by the helper over <c>automation.*</c>.</summary>
public sealed partial class AutomationsSettingsPage : Page
{
    public AutomationsSettingsViewModel ViewModel { get; } = new();

    public AutomationsSettingsPage()
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
}
