using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.KeyboardSettingsPage"/>. Spec §20.2 — every default
/// binding listed in the spec is materialized here so the panel can render even before the
/// helper hands back persisted user overrides.
/// </summary>
public sealed partial class KeybindingsViewModel : ObservableObject
{
    public ObservableCollection<KeybindingRowViewModel> Bindings { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private KeybindingRowViewModel? _selectedBinding;
    [ObservableProperty] private string _statusMessage = "Defaults loaded; persisted overrides apply on activation.";

    public KeybindingsViewModel()
    {
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        // Spec §20.2 — global / window / chat / editor / terminal default bindings.
        AddDefault("Send message", "chat", "Ctrl+Enter");
        AddDefault("Insert newline in composer", "chat", "Shift+Enter");
        AddDefault("Stop active turn", "chat", "Esc");
        AddDefault("Clarify cancel", "chat", "Esc");
        AddDefault("Plan confirm", "plan", "Ctrl+Shift+P");
        AddDefault("Plan request changes", "plan", "Ctrl+Shift+R");
        AddDefault("New session", "global", "Ctrl+N");
        AddDefault("Open Settings", "global", "Ctrl+,");
        AddDefault("Open Search", "global", "Ctrl+K");
        AddDefault("Open History", "global", "Ctrl+H");
        AddDefault("Toggle sandbox", "global", "Ctrl+Shift+S");
        AddDefault("Toggle theme (Light/Dark)", "global", "Ctrl+Shift+T");
        AddDefault("Quit application", "global", "Alt+F4");
        AddDefault("Save file", "editor", "Ctrl+S");
        AddDefault("Format document", "editor", "Shift+Alt+F");
        AddDefault("Open command palette", "global", "Ctrl+Shift+P");
        AddDefault("Focus terminal", "terminal", "Ctrl+`");
        AddDefault("Kill terminal", "terminal", "Ctrl+Shift+`");
        AddDefault("Cycle nav-rail destination", "global", "Ctrl+Tab");
        AddDefault("Reverse-cycle nav-rail destination", "global", "Ctrl+Shift+Tab");
    }

    private void AddDefault(string action, string category, string primary)
    {
        Bindings.Add(new KeybindingRowViewModel
        {
            Action = action,
            Category = category,
            Primary = primary,
            Secondary = string.Empty,
        });
    }

    [RelayCommand]
    private void Edit(KeybindingRowViewModel? row) =>
        StatusMessage = row is null ? "Select a binding to edit." : $"Recording new binding for {row.Action} (wire-up pending).";

    [RelayCommand]
    private void AddSecondary(KeybindingRowViewModel? row) =>
        StatusMessage = row is null ? "Select a binding first." : $"Recording secondary for {row.Action}.";

    [RelayCommand]
    private void Reset(KeybindingRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.Secondary = string.Empty;
        StatusMessage = $"Reset {row.Action} to default.";
    }

    [RelayCommand]
    private void Remove(KeybindingRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.Primary = string.Empty;
        row.Secondary = string.Empty;
        StatusMessage = $"Removed binding for {row.Action}.";
    }

    public IEnumerable<KeybindingRowViewModel> Filtered =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Bindings
            : Bindings.Where(b =>
                b.Action.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) ||
                b.Category.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) ||
                b.Primary.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase));
}

public sealed partial class KeybindingRowViewModel : ObservableObject
{
    [ObservableProperty] private string _action = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _primary = string.Empty;
    [ObservableProperty] private string _secondary = string.Empty;
}
