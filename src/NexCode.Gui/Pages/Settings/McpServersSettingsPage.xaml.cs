using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>MCP server registry panel (spec §17). Backed by the helper over <c>mcp.*</c>.</summary>
public sealed partial class McpServersSettingsPage : Page
{
    public McpSettingsViewModel ViewModel { get; } = new();

    public McpServersSettingsPage()
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

    private async void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // IsLoaded is false while the detail panel is binding a freshly selected/loaded row, so
        // this only fires for genuine user interaction rather than the initial value push.
        if (sender is ToggleSwitch { IsLoaded: true } toggle && toggle.DataContext is McpServerRowViewModel row)
        {
            await ViewModel.ToggleAsync(row, toggle.IsOn);
        }
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
        sheet.Saved += async (_, json) =>
        {
            row.ManifestJson = json;
            dialog.Hide();
            await ViewModel.SaveAsync(row);
        };

        await dialog.ShowAsync();
    }
}
