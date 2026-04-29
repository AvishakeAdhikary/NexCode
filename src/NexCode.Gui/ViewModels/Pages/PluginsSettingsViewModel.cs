using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/* contract pending: Slice 0017 PluginListResponse / PluginSummary not yet defined in
 * NexCode.Shared.Contracts. The records at the bottom of this file are local stubs. */

/// <summary>VM for <see cref="Gui.Pages.Settings.PluginsSettingsPage"/>.</summary>
public sealed partial class PluginsSettingsViewModel : ObservableObject
{
    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

    [ObservableProperty] private PluginRowViewModel? _selectedPlugin;
    [ObservableProperty] private string _statusMessage = "Installed plugins are loaded lazily.";

    [RelayCommand]
    private void InstallFromFile() => StatusMessage = "Pick a .nexplug to install (wire-up pending).";

    [RelayCommand]
    private void Uninstall(PluginRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Plugins.Remove(row);
        if (SelectedPlugin == row)
        {
            SelectedPlugin = null;
        }

        StatusMessage = $"Uninstalled {row.Name} (wire-up pending).";
    }

    [RelayCommand]
    private void Toggle(PluginRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsEnabled = !row.IsEnabled;
        StatusMessage = $"{row.Name} {(row.IsEnabled ? "enabled" : "disabled")} (wire-up pending).";
    }

    [RelayCommand]
    private void CheckUpdate(PluginRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select a plugin first."
            : $"Check {row.Name} for updates (wire-up pending).";
}

public sealed partial class PluginRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = "0.0.0";
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _permissions = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
}

/* Stub: pending wire-up. */
public sealed record PluginListResponse(PluginSummary[] Plugins);
public sealed record PluginSummary(
    Guid Id, string Name, string Version, string Author, string Description, string Permissions, bool IsEnabled);
