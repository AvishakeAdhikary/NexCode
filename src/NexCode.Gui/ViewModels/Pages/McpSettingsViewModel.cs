using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.McpServersSettingsPage"/>. Spec §17 — list / upsert /
/// remove / connect / disconnect MCP servers over the helper (<c>mcp.list/upsert/remove/connect/
/// disconnect</c>). The "Enabled" toggle maps to <c>auto_connect</c> and drives a live
/// connect/disconnect; the row's <c>Command</c> is folded into the connection config JSON.
/// </summary>
public sealed partial class McpSettingsViewModel : ObservableObject
{
    public ObservableCollection<McpServerRowViewModel> Servers { get; } = new();

    [ObservableProperty] private McpServerRowViewModel? _selectedServer;
    [ObservableProperty] private string _statusMessage = "MCP servers load lazily on first activation.";
    [ObservableProperty] private bool _isBusy;

    private HelperControlClient? _client;

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
            var response = await _client.ListMcpServersAsync();
            ApplyListResponse(response);
            StatusMessage = $"Loaded {Servers.Count} MCP server(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load MCP servers: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists the given row through <c>mcp.upsert</c> and reloads.</summary>
    public async Task SaveAsync(McpServerRowViewModel row)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot save.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.UpsertMcpServerAsync(row.ToUpsertRequest());
            await LoadAsync();
            StatusMessage = $"Saved {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddServer()
    {
        var row = new McpServerRowViewModel
        {
            Name = "new-mcp-server",
            Transport = McpTransportTypes.Stdio,
            Command = string.Empty,
            ManifestJson = "{}",
            Status = McpServerStatuses.Disconnected,
        };
        Servers.Add(row);
        SelectedServer = row;
        await SaveAsync(row);
    }

    [RelayCommand]
    private async Task DeleteServer(McpServerRowViewModel? row)
    {
        row ??= SelectedServer;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Servers.Remove(row);
            if (SelectedServer == row)
            {
                SelectedServer = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (row.Id != Guid.Empty)
            {
                await _client.RemoveMcpServerAsync(row.Id);
            }
            await LoadAsync();
            StatusMessage = $"Removed {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestServer(McpServerRowViewModel? row)
    {
        row ??= SelectedServer;
        if (row is null)
        {
            StatusMessage = "Select an MCP server first.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot connect.";
            return;
        }

        if (row.Id == Guid.Empty)
        {
            StatusMessage = "Save the server before connecting.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.ConnectMcpServerAsync(row.Id);
            await LoadAsync();
            StatusMessage = $"Connect requested for {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connect failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Invoked when a row's Enabled toggle flips: persists the new auto-connect flag and
    /// issues a live connect/disconnect. The list is not reloaded here so the toggle the user
    /// just flipped is not re-bound out from under them; the row's status is updated in place.
    /// </summary>
    public async Task ToggleAsync(McpServerRowViewModel row, bool enabled)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot toggle.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _client.UpsertMcpServerAsync(row.ToUpsertRequest());
            if (row.Id == Guid.Empty)
            {
                row.Id = result.Id;
            }

            if (enabled)
            {
                await _client.ConnectMcpServerAsync(row.Id);
                row.Status = McpServerStatuses.Connecting;
            }
            else
            {
                await _client.DisconnectMcpServerAsync(row.Id);
                row.Status = McpServerStatuses.Disconnected;
            }

            StatusMessage = $"{row.Name} {(enabled ? "connecting" : "disconnected")}.";
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

    public void ApplyListResponse(McpListResponse response)
    {
        Servers.Clear();
        foreach (var s in response.Servers)
        {
            Servers.Add(McpServerRowViewModel.FromSummary(s));
        }
    }
}

public sealed partial class McpServerRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _transport = McpTransportTypes.Stdio;
    [ObservableProperty] private string _command = string.Empty;
    [ObservableProperty] private string _manifestJson = "{}";
    [ObservableProperty] private string _status = McpServerStatuses.Disconnected;
    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>
    /// Builds the wire request. The connection config JSON carries the full manifest; when the
    /// user only typed a Command and left the manifest empty, a minimal config is synthesized so
    /// no information is lost on round-trip.
    /// </summary>
    public McpUpsertRequest ToUpsertRequest()
    {
        var config = string.IsNullOrWhiteSpace(ManifestJson) || ManifestJson.Trim() == "{}"
            ? (string.IsNullOrWhiteSpace(Command)
                ? "{}"
                : System.Text.Json.JsonSerializer.Serialize(new { command = Command }))
            : ManifestJson;

        return new McpUpsertRequest(
            Name,
            Transport,
            config,
            IsEnabled,
            Id == Guid.Empty ? null : Id);
    }

    public static McpServerRowViewModel FromSummary(McpServerSummary s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Transport = s.Type,
        Command = ExtractCommand(s.ConnectionConfigJson),
        ManifestJson = string.IsNullOrWhiteSpace(s.ConnectionConfigJson) ? "{}" : s.ConnectionConfigJson,
        Status = s.Status,
        IsEnabled = s.AutoConnect,
    };

    private static string ExtractCommand(string? connectionConfigJson)
    {
        if (string.IsNullOrWhiteSpace(connectionConfigJson))
        {
            return string.Empty;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(connectionConfigJson);
            return doc.RootElement.TryGetProperty("command", out var command)
                ? command.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }
}
