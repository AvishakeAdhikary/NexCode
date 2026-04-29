using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/* contract pending: HistoryListResponse / SessionHistorySummary not yet shipped — stubs below. */

/// <summary>VM for <see cref="Gui.Pages.HistoryPage"/>. Spec §15.5 / §32 (History panel).</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<SessionHistoryRowViewModel> Sessions { get; } = new();

    public string[] DateFilters { get; } = { "all", "today", "this-week", "this-month", "older" };

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _projectFilter = string.Empty;
    [ObservableProperty] private string _modeFilter = string.Empty;
    [ObservableProperty] private string _providerFilter = string.Empty;
    [ObservableProperty] private string _personalityFilter = string.Empty;
    [ObservableProperty] private string _dateFilter = "all";
    [ObservableProperty] private SessionHistoryRowViewModel? _selectedSession;
    [ObservableProperty] private string _statusMessage = "History loads on activation.";

    [RelayCommand]
    private void Open(SessionHistoryRowViewModel? row) =>
        StatusMessage = row is null ? "Select a session to open." : $"Opening {row.Title} (wire-up pending).";

    [RelayCommand]
    private void Archive(SessionHistoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsArchived = true;
        StatusMessage = $"Archived {row.Title}.";
    }

    [RelayCommand]
    private void ExportJson(SessionHistoryRowViewModel? row) =>
        StatusMessage = row is null ? "Select a session." : $"Export {row.Title} as JSON.";

    [RelayCommand]
    private void ExportMarkdown(SessionHistoryRowViewModel? row) =>
        StatusMessage = row is null ? "Select a session." : $"Export {row.Title} as Markdown.";

    [RelayCommand]
    private void Rename(SessionHistoryRowViewModel? row) =>
        StatusMessage = row is null ? "Select a session." : $"Rename {row.Title} (wire-up pending).";

    [RelayCommand]
    private void Delete(SessionHistoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Sessions.Remove(row);
        if (SelectedSession == row)
        {
            SelectedSession = null;
        }
    }

    [RelayCommand]
    private void Search() => StatusMessage = $"Search '{SearchText}' (wire-up pending).";
}

public sealed partial class SessionHistoryRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _project = string.Empty;
    [ObservableProperty] private string _mode = string.Empty;
    [ObservableProperty] private string _provider = string.Empty;
    [ObservableProperty] private string _personality = string.Empty;
    [ObservableProperty] private DateTimeOffset _updatedAt;
    [ObservableProperty] private int _messageCount;
    [ObservableProperty] private bool _isArchived;
}

/* Stub: pending wire-up. */
public sealed record HistoryListResponse(SessionHistorySummary[] Sessions);
public sealed record SessionHistorySummary(
    Guid Id, string Title, string Project, string Mode, string Provider, string Personality,
    DateTimeOffset UpdatedAt, int MessageCount, bool IsArchived);
