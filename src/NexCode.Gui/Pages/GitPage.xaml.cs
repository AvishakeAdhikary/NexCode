using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>
/// Spec §16 Git Manager page. Hosts <see cref="GitPageViewModel"/> via the page resource
/// and provides the surface for working tree, branches, remotes, stashes, tags, and log.
/// </summary>
public sealed partial class GitPage : Page
{
    public GitPage()
    {
        InitializeComponent();
    }

    public GitPageViewModel ViewModel => (GitPageViewModel)Resources["ViewModel"];
}
