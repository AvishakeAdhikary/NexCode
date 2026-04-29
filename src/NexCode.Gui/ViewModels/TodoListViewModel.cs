using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// View model for a todo list artifact card. Owns the per-item commands: toggle/skip/delete/add.
/// </summary>
public sealed partial class TodoListViewModel : ObservableViewModelBase
{
    public TodoListViewModel()
        : this(Guid.Empty, "Untitled list")
    {
    }

    public TodoListViewModel(Guid listId, string title)
    {
        ListId = listId;
        _title = title;
        Items = [];
        Items.CollectionChanged += (_, _) => RecomputeProgress();
    }

    public Guid ListId { get; private set; }

    [ObservableProperty]
    private string _title;

    public ObservableCollection<TodoItemViewModel> Items { get; }

    [ObservableProperty]
    private string _progress = "0/0 tasks done";

    [ObservableProperty]
    private bool _showAll;

    public int DoneCount => Items.Count(i => i.Status == TodoItemStatus.Done);
    public int SkippedCount => Items.Count(i => i.Status == TodoItemStatus.Skipped);
    public int TotalCount => Items.Count;

    public double ProgressFraction =>
        TotalCount == 0 ? 0d : (double)DoneCount / TotalCount;

    public event EventHandler<TodoMutation>? MutationRequested;

    public void RecomputeProgress()
    {
        var skipNote = SkippedCount > 0 ? $" ({SkippedCount} skipped)" : string.Empty;
        Progress = $"{DoneCount}/{TotalCount} tasks done{skipNote}";
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ProgressFraction));
    }

    [RelayCommand]
    private void Toggle(TodoItemViewModel? item)
    {
        if (item is null) return;
        item.IsDone = !item.IsDone;
        RecomputeProgress();
        MutationRequested?.Invoke(this, new TodoMutation(ListId, item.ItemId, TodoMutationKind.Toggle, item.Text));
    }

    [RelayCommand]
    private void Skip(TodoItemViewModel? item)
    {
        if (item is null) return;
        item.Status = TodoItemStatus.Skipped;
        RecomputeProgress();
        MutationRequested?.Invoke(this, new TodoMutation(ListId, item.ItemId, TodoMutationKind.Skip, item.Text));
    }

    [RelayCommand]
    private void Delete(TodoItemViewModel? item)
    {
        if (item is null) return;
        Items.Remove(item);
        MutationRequested?.Invoke(this, new TodoMutation(ListId, item.ItemId, TodoMutationKind.Delete, item.Text));
    }

    [RelayCommand]
    private void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var item = new TodoItemViewModel(Guid.NewGuid(), text.Trim(), TodoItemStatus.Pending);
        Items.Add(item);
        MutationRequested?.Invoke(this, new TodoMutation(ListId, item.ItemId, TodoMutationKind.Add, item.Text));
    }
}

public enum TodoMutationKind
{
    Toggle,
    Skip,
    Delete,
    Add
}

public sealed record TodoMutation(Guid ListId, Guid ItemId, TodoMutationKind Kind, string Text);
