using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.HistoryPage"/>. Spec §15.5 / §32 (History panel). Backed by the
/// helper transport (<c>history.list/search/export/archive/delete</c>) via
/// <see cref="HelperControlClient"/>.
/// </summary>
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
    [ObservableProperty] private bool _isBusy;

    private HelperControlClient? _client;

    /// <summary>
    /// Supplied by the page (which owns the window handle) to persist exported content to a
    /// file via a save picker. Returns the saved path, or <c>null</c> if the user cancelled.
    /// </summary>
    public Func<string, string, string, Task<string?>>? SaveExportHandler { get; set; }

    /// <summary>Called by the page on activation: binds the helper transport and loads the list.</summary>
    public async Task InitializeAsync(HelperControlClient client)
    {
        _client = client;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var projectFilter = string.IsNullOrWhiteSpace(ProjectFilter) ? null : ProjectFilter;
            var response = await _client.ListHistoryAsync(null, projectFilter);
            ApplyListResponse(response);
            StatusMessage = Sessions.Count == 0
                ? "No sessions in history yet."
                : $"Loaded {Sessions.Count} session(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Search()
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot search.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            await LoadAsync();
            return;
        }

        IsBusy = true;
        try
        {
            var response = await _client.SearchHistoryAsync(SearchText);
            StatusMessage = $"Found {response.Results.Length} match(es) for '{SearchText}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Open(SessionHistoryRowViewModel? row) =>
        StatusMessage = row is null ? "Select a session to open." : $"Opening {row.Title} (wire-up pending).";

    [RelayCommand]
    private async Task Archive(SessionHistoryRowViewModel? row)
    {
        if (row is null || _client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _client.ArchiveHistoryAsync(row.Id);
            row.IsArchived = true;
            StatusMessage = $"Archived {row.Title}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Archive failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ExportJson(SessionHistoryRowViewModel? row) => ExportAsync(row, "json");

    [RelayCommand]
    private Task ExportMarkdown(SessionHistoryRowViewModel? row) => ExportAsync(row, "markdown");

    private async Task ExportAsync(SessionHistoryRowViewModel? row, string format)
    {
        if (row is null)
        {
            StatusMessage = "Select a session.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot export.";
            return;
        }

        IsBusy = true;
        try
        {
            var export = await _client.ExportHistoryAsync(row.Id, format);

            if (SaveExportHandler is null)
            {
                StatusMessage = $"Exported {row.Title} ({export.Content.Length} chars); no save target available.";
                return;
            }

            var suggestedName = $"session-{row.Id}";
            var savedPath = await SaveExportHandler(export.Content, suggestedName, format);
            StatusMessage = savedPath is null
                ? "Export cancelled."
                : $"Exported {row.Title} to {savedPath}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Rename(SessionHistoryRowViewModel? row) =>
        // No rename IPC exists yet; keep the status honest until a handler ships.
        StatusMessage = row is null ? "Select a session." : $"Rename {row.Title} (no backend handler yet).";

    [RelayCommand]
    private async Task Delete(SessionHistoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Sessions.Remove(row);
            if (SelectedSession == row)
            {
                SelectedSession = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            await _client.DeleteHistoryAsync(row.Id);
            await LoadAsync();
            StatusMessage = $"Deleted {row.Title}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ApplyListResponse(HistoryListResponse response)
    {
        Sessions.Clear();
        foreach (var s in response.Sessions)
        {
            Sessions.Add(SessionHistoryRowViewModel.FromSummary(s));
        }
    }
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

    public static SessionHistoryRowViewModel FromSummary(HistorySummary s) => new()
    {
        Id = s.SessionId,
        Title = string.IsNullOrWhiteSpace(s.Title) ? "(untitled session)" : s.Title!,
        Project = s.ProjectPath,
        Mode = s.Mode,
        Provider = s.ProviderKey,
        Personality = string.Empty,
        UpdatedAt = s.UpdatedAt,
        MessageCount = s.MessageCount,
        IsArchived = false,
    };
}
