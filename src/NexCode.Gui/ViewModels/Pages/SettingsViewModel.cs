using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// Top-level VM for <see cref="Gui.Pages.SettingsPage"/>. Owns the list of settings panels
/// surfaced through the inner <see cref="Microsoft.UI.Xaml.Controls.NavigationView"/> and the
/// currently selected panel tag (used by the page to navigate the inner Frame).
/// Spec §32 — every entry corresponds to a sub-page under <c>Pages\Settings</c>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public ObservableCollection<SettingsPanelEntry> Panels { get; } =
        new()
        {
            new("general", "General", ""),
            new("account", "Account", ""),
            new("providers", "Providers", ""),
            new("modes", "Modes", ""),
            new("personalities", "Personalities", ""),
            new("memory", "Memory", ""),
            new("appearance", "Appearance", ""),
            new("keyboard", "Keyboard", ""),
            new("notifications", "Notifications", ""),
            new("ssh", "SSH Keys", ""),
            new("mcp", "MCP Servers", ""),
            new("plugins", "Plugins", ""),
            new("automations", "Automations", ""),
            new("environments", "Environments", ""),
            new("editor", "Editor", ""),
            new("privacy", "Privacy", ""),
            new("shell", "Shell", ""),
            new("plans", "Plans", ""),
            new("about", "About", ""),
            new("advanced", "Advanced", ""),
        };

    [ObservableProperty]
    private SettingsPanelEntry? _selectedPanel;

    public SettingsViewModel()
    {
        _selectedPanel = Panels[0];
    }
}

/// <summary>One row in the settings nav-view sidebar.</summary>
public sealed record SettingsPanelEntry(string Tag, string Title, string IconGlyph);
