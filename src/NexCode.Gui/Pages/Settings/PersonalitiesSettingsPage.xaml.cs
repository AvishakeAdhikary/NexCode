using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Personality CRUD panel (spec §8.2).</summary>
public sealed partial class PersonalitiesSettingsPage : Page
{
    public PersonalitiesSettingsViewModel ViewModel { get; } = new();

    public PersonalitiesSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
