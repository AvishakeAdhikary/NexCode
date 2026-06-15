using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>
/// Spec §16 Git Manager page. Hosts <see cref="GitPageViewModel"/> via the page resource
/// and provides the surface for working tree, diff, and checkpoint revert. Only the
/// backend-supported verbs (git.status / git.diff / git.revert) are wired.
/// </summary>
public sealed partial class GitPage : Page
{
    public GitPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public GitPageViewModel ViewModel => (GitPageViewModel)Resources["ViewModel"];

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var client = ((App)Application.Current).Services.GetRequiredService<HelperControlClient>();
        await ViewModel.InitializeAsync(client);
    }
}
