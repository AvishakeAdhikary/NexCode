using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>MCP server registry panel (spec §32 — slice 0016 wire-up pending).</summary>
public sealed partial class McpServersSettingsPage : Page
{
    public McpSettingsViewModel ViewModel { get; } = new();

    public McpServersSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void OpenManifest_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedServer is not { } row)
        {
            return;
        }

        var sheet = new JsonEditorSheet { Title = $"MCP manifest: {row.Name}" };
        sheet.SetBaseline(row.ManifestJson);

        var dialog = new ContentDialog
        {
            Title = "Edit MCP manifest",
            Content = sheet,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };

        sheet.Closed += (_, _) => dialog.Hide();
        sheet.Saved += (_, json) =>
        {
            row.ManifestJson = json;
            ViewModel.StatusMessage = $"Saved manifest for {row.Name} (in-memory; helper sync pending).";
            dialog.Hide();
        };

        await dialog.ShowAsync();
    }
}
