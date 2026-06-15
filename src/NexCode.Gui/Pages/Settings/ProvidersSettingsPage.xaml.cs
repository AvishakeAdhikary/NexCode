using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Provider list / edit / set-default panel (spec §32). Loads from and persists to the helper
/// via <see cref="HelperControlClient"/> (<c>provider.list/upsert/remove/set_default</c>).
/// </summary>
public sealed partial class ProvidersSettingsPage : Page
{
    public ProvidersSettingsViewModel ViewModel { get; } = new();

    public ProvidersSettingsPage()
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
