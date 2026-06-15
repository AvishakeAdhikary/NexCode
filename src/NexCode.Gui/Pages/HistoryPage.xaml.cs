using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NexCode.Gui.Pages;

/// <summary>Nav-rail History destination (spec §15.5 / §32).</summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.SaveExportHandler = SaveExportAsync;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var client = ((App)Application.Current).Services.GetRequiredService<HelperControlClient>();
        await ViewModel.InitializeAsync(client);
    }

    private static async Task<string?> SaveExportAsync(string content, string suggestedName, string format)
    {
        var isMarkdown = string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase);
        var extension = isMarkdown ? ".md" : ".json";

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(format.ToUpperInvariant(), new[] { extension });

        if (((App)Application.Current).MainWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await FileIO.WriteTextAsync(file, content);
        return file.Path;
    }
}
