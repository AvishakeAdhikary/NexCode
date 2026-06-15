using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Custom mode CRUD with a JSON editor sheet for advanced shape edits (spec §7.1).</summary>
public sealed partial class ModesSettingsPage : Page
{
    public ModesSettingsViewModel ViewModel { get; } = new();

    public ModesSettingsPage()
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

    private async void OpenJsonEditor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedMode is not { } row)
        {
            return;
        }

        var sheet = new JsonEditorSheet { Title = $"Mode: {row.Name}" };
        sheet.SetBaseline(JsonSerializer.Serialize(row.ToUpsertRequest(),
            new JsonSerializerOptions { WriteIndented = true }));

        var dialog = new ContentDialog
        {
            Title = "Edit mode JSON",
            Content = sheet,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };

        sheet.Closed += (_, _) => dialog.Hide();
        sheet.Saved += async (_, json) =>
        {
            dialog.Hide();
            await ViewModel.ApplyEditedJsonAsync(row, json);
        };

        await dialog.ShowAsync();
    }
}
