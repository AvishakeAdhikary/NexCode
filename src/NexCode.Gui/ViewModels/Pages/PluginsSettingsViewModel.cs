using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.PluginsSettingsPage"/> and <see cref="Gui.Pages.PluginsPage"/>.
/// Spec §22.1 — list / install / uninstall / toggle plugins over the helper
/// (<c>plugin.list/install/uninstall/toggle</c>). Author / description / permissions are surfaced
/// best-effort from the plugin manifest JSON since the list contract carries the raw manifest.
/// </summary>
public sealed partial class PluginsSettingsViewModel : ObservableObject
{
    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

    [ObservableProperty] private PluginRowViewModel? _selectedPlugin;
    [ObservableProperty] private string _statusMessage = "Installed plugins load lazily on first activation.";
    [ObservableProperty] private bool _isBusy;

    private HelperControlClient? _client;

    /// <summary>
    /// Set by the hosting page so the VM can show a file picker for installs without taking a
    /// dependency on the UI thread / HWND itself. Returns the chosen <c>.nexplug</c> path or null.
    /// </summary>
    public Func<Task<string?>>? PickPluginPackageAsync { get; set; }

    /// <summary>Called by the page on activation: binds the helper transport and loads the list.</summary>
    public async Task InitializeAsync(HelperControlClient client)
    {
        _client = client;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Reload() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var response = await _client.ListPluginsAsync();
            ApplyListResponse(response);
            StatusMessage = $"Loaded {Plugins.Count} plugin(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load plugins: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallFromFile()
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot install.";
            return;
        }

        if (PickPluginPackageAsync is null)
        {
            StatusMessage = "No file picker is available on this view.";
            return;
        }

        var path = await PickPluginPackageAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Install cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            var summary = await _client.InstallPluginAsync(path);
            await LoadAsync();
            StatusMessage = $"Installed {summary.Name} {summary.Version}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Uninstall(PluginRowViewModel? row)
    {
        row ??= SelectedPlugin;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Plugins.Remove(row);
            if (SelectedPlugin == row)
            {
                SelectedPlugin = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (row.Id != Guid.Empty)
            {
                await _client.UninstallPluginAsync(row.Id);
            }
            await LoadAsync();
            StatusMessage = $"Uninstalled {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Uninstall failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Toggle(PluginRowViewModel? row)
    {
        row ??= SelectedPlugin;
        if (row is null)
        {
            return;
        }

        await SetEnabledAsync(row, !row.IsEnabled);
    }

    /// <summary>Persists the plugin's enabled flag through <c>plugin.toggle</c>.</summary>
    public async Task SetEnabledAsync(PluginRowViewModel row, bool enabled)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot toggle.";
            return;
        }

        if (row.Id == Guid.Empty)
        {
            StatusMessage = "Plugin must be installed before toggling.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.TogglePluginAsync(row.Id, enabled);
            row.IsEnabled = enabled;
            StatusMessage = $"{row.Name} {(enabled ? "enabled" : "disabled")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Toggle failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CheckUpdate(PluginRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select a plugin first."
            : $"{row.Name} is at version {row.Version}. Reinstall a newer .nexplug to update.";

    public void ApplyListResponse(PluginListResponse response)
    {
        Plugins.Clear();
        foreach (var p in response.Plugins)
        {
            Plugins.Add(PluginRowViewModel.FromSummary(p));
        }
    }
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

    public static PluginRowViewModel FromSummary(PluginSummary s)
    {
        var (author, description, permissions) = ParseManifest(s.ManifestJson);
        return new PluginRowViewModel
        {
            Id = s.Id,
            Name = s.Name,
            Version = s.Version,
            Author = author,
            Description = description,
            Permissions = permissions,
            IsEnabled = s.Enabled,
        };
    }

    /// <summary>Pulls display metadata out of the manifest JSON; missing fields render blank.</summary>
    private static (string Author, string Description, string Permissions) ParseManifest(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;
            var author = root.TryGetProperty("author", out var a) ? a.GetString() ?? string.Empty : string.Empty;
            var description = root.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var permissions = string.Empty;
            if (root.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Array)
            {
                var items = new System.Collections.Generic.List<string>();
                foreach (var item in p.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        items.Add(item.GetString() ?? string.Empty);
                    }
                }
                permissions = string.Join(", ", items);
            }

            return (author, description, permissions);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }
}
