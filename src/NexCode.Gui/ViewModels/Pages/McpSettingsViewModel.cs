using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/* contract pending: Slice 0016 has not finalized McpListResponse / McpServerSummary in
 * NexCode.Shared.Contracts. The minimal record types below are temporary local stubs that
 * mirror the shape expected from `mcp.list` / `mcp.upsert` so the panel can compile and
 * render today; they will be replaced with the canonical contracts when those land. */

/// <summary>VM for <see cref="Gui.Pages.Settings.McpServersSettingsPage"/>.</summary>
public sealed partial class McpSettingsViewModel : ObservableObject
{
    public ObservableCollection<McpServerRowViewModel> Servers { get; } = new();

    [ObservableProperty] private McpServerRowViewModel? _selectedServer;
    [ObservableProperty] private string _statusMessage = "MCP server list (wire-up pending).";

    [RelayCommand]
    private void AddServer()
    {
        var row = new McpServerRowViewModel
        {
            Id = Guid.NewGuid(),
            Name = "new-mcp-server",
            Transport = "stdio",
            Command = string.Empty,
            ManifestJson = "{}",
            Status = "disconnected",
        };
        Servers.Add(row);
        SelectedServer = row;
    }

    [RelayCommand]
    private void DeleteServer(McpServerRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Servers.Remove(row);
        if (SelectedServer == row)
        {
            SelectedServer = null;
        }
    }

    [RelayCommand]
    private void TestServer(McpServerRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select an MCP server first."
            : $"Connect-test {row.Name} (wire-up pending).";

    [RelayCommand]
    private void EditManifest(McpServerRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select an MCP server first."
            : $"Open JSON editor for {row.Name}.";
}

public sealed partial class McpServerRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _transport = "stdio";
    [ObservableProperty] private string _command = string.Empty;
    [ObservableProperty] private string _manifestJson = "{}";
    [ObservableProperty] private string _status = "disconnected";
    [ObservableProperty] private bool _isEnabled = true;
}

/* Stub: pending wire-up — minimal shape echoed back by the helper. */
public sealed record McpListResponse(McpServerSummary[] Servers);
public sealed record McpServerSummary(
    Guid Id, string Name, string Transport, string Command, string ManifestJson, string Status, bool IsEnabled);
