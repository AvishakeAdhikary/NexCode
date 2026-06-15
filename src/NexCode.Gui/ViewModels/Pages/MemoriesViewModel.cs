using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.MemoriesPage"/>. Spec §9 — full memory CRUD UI backed by the
/// helper transport (<c>memory.list/write/delete</c>) via <see cref="HelperControlClient"/>.
/// </summary>
public sealed partial class MemoriesViewModel : ObservableObject
{
    public ObservableCollection<MemoryRowViewModel> Memories { get; } = new();

    public MemoryScope[] ScopeOptions { get; } = { MemoryScope.Global, MemoryScope.Project, MemoryScope.Session };

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MemoryScope? _scopeFilter;
    [ObservableProperty] private MemoryRowViewModel? _selectedMemory;
    [ObservableProperty] private string _statusMessage = "Memory list loads on activation.";
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
            var response = await _client.ListMemoriesAsync();
            ApplyListResponse(response);
            StatusMessage = Memories.Count == 0
                ? "No memories yet. Add one and save to inject it into prompts."
                : $"Loaded {Memories.Count} memory(ies).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load memories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddMemory()
    {
        var row = new MemoryRowViewModel
        {
            Id = Guid.NewGuid(),
            Key = "new-memory",
            Value = string.Empty,
            Scope = MemoryScope.Global,
            Tags = Array.Empty<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow,
        };
        Memories.Add(row);
        SelectedMemory = row;
        StatusMessage = "Fill in the details and click Save to persist this memory.";
    }

    [RelayCommand]
    private async Task Save(MemoryRowViewModel? row)
    {
        row ??= SelectedMemory;
        if (row is null)
        {
            StatusMessage = "Select a memory to save.";
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Key))
        {
            StatusMessage = "A memory key is required.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot save.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.WriteMemoryAsync(row.Key, row.Value, row.Scope, row.ProjectId, row.SessionId, row.Tags);
            await LoadAsync();
            StatusMessage = $"Saved {row.Key}.";
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
    private async Task Delete(MemoryRowViewModel? row)
    {
        row ??= SelectedMemory;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Memories.Remove(row);
            if (SelectedMemory == row)
            {
                SelectedMemory = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            await _client.DeleteMemoryAsync(row.Id);
            await LoadAsync();
            StatusMessage = $"Deleted {row.Key}.";
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

    [RelayCommand]
    private void CopyUri(MemoryRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select a memory."
            : $"Copied nexcode://memory/{row.Scope}/{row.Key} to clipboard (wire-up pending).";

    [RelayCommand]
    private void Export() => StatusMessage = "Export memories (wire-up pending).";

    [RelayCommand]
    private void Import() => StatusMessage = "Import memories (wire-up pending).";

    public void ApplyListResponse(MemoryListResponse response)
    {
        Memories.Clear();
        foreach (var m in response.Memories)
        {
            Memories.Add(MemoryRowViewModel.FromSummary(m));
        }
    }
}

public sealed partial class MemoryRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private MemoryScope _scope;
    [ObservableProperty] private Guid? _projectId;
    [ObservableProperty] private Guid? _sessionId;
    [ObservableProperty] private string[] _tags = Array.Empty<string>();
    [ObservableProperty] private DateTimeOffset _createdAt;
    [ObservableProperty] private DateTimeOffset _lastAccessedAt;

    public string TagsCsv => string.Join(", ", Tags);

    public MemoryWriteRequest ToWriteRequest() =>
        new(Key, Value, Scope, ProjectId, SessionId, Tags);

    public static MemoryRowViewModel FromSummary(MemorySummary s) => new()
    {
        Id = s.Id,
        Key = s.Key,
        Value = s.Value,
        Scope = s.Scope,
        ProjectId = s.ProjectId,
        SessionId = s.SessionId,
        Tags = s.Tags,
        CreatedAt = s.CreatedAt,
        LastAccessedAt = s.LastAccessedAt,
    };
}
