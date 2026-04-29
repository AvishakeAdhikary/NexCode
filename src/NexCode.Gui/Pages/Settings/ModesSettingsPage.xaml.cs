using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        sheet.Saved += (_, _) =>
        {
            ViewModel.StatusMessage = "Mode JSON saved (in-memory; helper sync wire-up pending).";
            dialog.Hide();
        };

        await dialog.ShowAsync();
    }
}
