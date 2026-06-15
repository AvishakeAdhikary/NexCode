using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>
/// Nav-rail Plans destination (spec §10). Markdown rendering of the selected plan happens
/// at preview-fidelity here; full Markdig rendering will be wired through a shared converter
/// once the GUI core's <c>Controls\PlanArtifactCard</c> ships.
/// </summary>
public sealed partial class PlansPage : Page
{
    public PlansPageViewModel ViewModel { get; } = new();

    public PlansPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Plans are per-session and this page has no session context of its own, so the VM
        // shows an empty-state prompt until a session id is supplied via LoadAsync.
        var client = ((App)Application.Current).Services.GetRequiredService<HelperControlClient>();
        await ViewModel.InitializeAsync(client);
    }
}
