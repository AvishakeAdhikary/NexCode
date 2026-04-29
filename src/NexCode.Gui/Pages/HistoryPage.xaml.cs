using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>Nav-rail History destination (spec §15.5 / §32).</summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
