using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Provider list / edit / set-default panel (spec §32). Wire-up to <c>provider.list/upsert/
/// remove/set_default</c> happens through <see cref="Services.HelperControlClient"/> once DI
/// is in place — the page itself stays declarative.
/// </summary>
public sealed partial class ProvidersSettingsPage : Page
{
    public ProvidersSettingsViewModel ViewModel { get; } = new();

    public ProvidersSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
