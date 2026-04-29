using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>Nav-rail Memories destination (spec §9 / §32).</summary>
public sealed partial class MemoriesPage : Page
{
    public MemoriesViewModel ViewModel { get; } = new();

    public MemoriesPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
