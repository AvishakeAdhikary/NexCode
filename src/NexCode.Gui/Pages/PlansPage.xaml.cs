using Microsoft.UI.Xaml.Controls;
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
    }
}
