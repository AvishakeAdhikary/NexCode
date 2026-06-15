using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NexCode.Gui.Pages.Settings;

/// <summary>Plugin registry panel (spec §22.1). Backed by the helper over <c>plugin.*</c>.</summary>
public sealed partial class PluginsSettingsPage : Page
{
    public PluginsSettingsViewModel ViewModel { get; } = new();

    public PluginsSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PickPluginPackageAsync = PickPluginPackageAsync;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var client = ((App)Application.Current).Services.GetRequiredService<HelperControlClient>();
        await ViewModel.InitializeAsync(client);
    }

    private async void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true } toggle && toggle.DataContext is PluginRowViewModel row)
        {
            await ViewModel.SetEnabledAsync(row, toggle.IsOn);
        }
    }

    private async Task<string?> PickPluginPackageAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".nexplug");
        picker.FileTypeFilter.Add("*");

        if (((App)Application.Current).MainWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
