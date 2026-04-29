using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.AppearanceSettingsPage"/>. Owns theme, accent color,
/// font scale, and message density. Theme apply is deferred to the GUI core's
/// <c>Services.ThemeService</c> when it ships.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject
{
    public ObservableCollection<string> Themes { get; } = new() { "System", "Dark", "Light", "HighContrast" };

    public ObservableCollection<string> Densities { get; } = new() { "comfortable", "compact" };

    public ObservableCollection<string> AccentSwatches { get; } = new()
    {
        "#2F81F7", "#7C3AED", "#10B981", "#F97316", "#EF4444", "#0EA5E9",
    };

    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private string _selectedDensity = "comfortable";
    [ObservableProperty] private string _selectedAccent = "#2F81F7";
    [ObservableProperty] private double _fontScale = 1.0;
    [ObservableProperty] private string _statusMessage = "Theme changes apply via the ThemeService when wired.";

    [RelayCommand]
    private void Apply() => StatusMessage = $"Applied theme={SelectedTheme}, accent={SelectedAccent}, scale={FontScale:0.00}.";

    [RelayCommand]
    private void Reset()
    {
        SelectedTheme = "System";
        SelectedDensity = "comfortable";
        SelectedAccent = "#2F81F7";
        FontScale = 1.0;
        StatusMessage = "Reset appearance to defaults.";
    }
}
