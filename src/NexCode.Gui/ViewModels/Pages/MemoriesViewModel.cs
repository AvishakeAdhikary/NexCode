using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>VM for <see cref="Gui.Pages.MemoriesPage"/>. Spec §9 — full memory CRUD UI.</summary>
public sealed partial class MemoriesViewModel : ObservableObject
{
    public ObservableCollection<MemoryRowViewModel> Memories { get; } = new();

    public MemoryScope[] ScopeOptions { get; } = { MemoryScope.Global, MemoryScope.Project, MemoryScope.Session };

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MemoryScope? _scopeFilter;
    [ObservableProperty] private MemoryRowViewModel? _selectedMemory;
    [ObservableProperty] private string _statusMessage = "Memory list loads on activation.";

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
    }

    [RelayCommand]
    private void Delete(MemoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Memories.Remove(row);
        if (SelectedMemory == row)
        {
            SelectedMemory = null;
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
