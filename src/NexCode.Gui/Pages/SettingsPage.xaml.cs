using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Pages.Settings;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages;

/// <summary>
/// Settings hub page (spec §32). Hosts an inner <see cref="NavigationView"/> whose menu items
/// are sourced from <see cref="SettingsViewModel"/> and whose <see cref="Frame"/> hosts each
/// settings sub-page. The hub itself owns no business logic; all CRUD lives inside the
/// individual panels' view-models.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private static readonly Dictionary<string, Type> PanelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["general"] = typeof(GeneralSettingsPage),
        ["account"] = typeof(AccountSettingsPage),
        ["providers"] = typeof(ProvidersSettingsPage),
        ["modes"] = typeof(ModesSettingsPage),
        ["personalities"] = typeof(PersonalitiesSettingsPage),
        ["memory"] = typeof(MemorySettingsPage),
        ["appearance"] = typeof(AppearanceSettingsPage),
        ["keyboard"] = typeof(KeyboardSettingsPage),
        ["notifications"] = typeof(NotificationsSettingsPage),
        ["ssh"] = typeof(SshKeysSettingsPage),
        ["mcp"] = typeof(McpServersSettingsPage),
        ["plugins"] = typeof(PluginsSettingsPage),
        ["automations"] = typeof(AutomationsSettingsPage),
        ["environments"] = typeof(EnvironmentsSettingsPage),
        ["editor"] = typeof(EditorSettingsPage),
        ["privacy"] = typeof(PrivacySettingsPage),
        ["shell"] = typeof(ShellSettingsPage),
        ["plans"] = typeof(PlansSettingsPage),
        ["about"] = typeof(AboutSettingsPage),
        ["advanced"] = typeof(AdvancedSettingsPage),
    };

    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPanel is { } panel)
        {
            NavigateToPanel(panel.Tag);
        }
    }

    private void SettingsNavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag })
        {
            NavigateToPanel(tag);
        }
    }

    private void NavigateToPanel(string tag)
    {
        if (PanelMap.TryGetValue(tag, out var pageType))
        {
            SettingsContentFrame.Navigate(pageType);
        }
    }
}
