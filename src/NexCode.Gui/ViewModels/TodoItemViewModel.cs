using CommunityToolkit.Mvvm.ComponentModel;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// A single todo line item bound by the todo artifact card (spec §11).
/// </summary>
public sealed partial class TodoItemViewModel : ObservableViewModelBase
{
    // Segoe Fluent Icons glyph code points used by the row indicator.
    private const string GlyphPending = "";    // CheckboxComposite
    private const string GlyphInProgress = ""; // Sync
    private const string GlyphDone = "";       // CheckMark
    private const string GlyphSkipped = "";    // SkipForward

    public TodoItemViewModel()
        : this(Guid.Empty, "New task", TodoItemStatus.Pending)
    {
    }

    public TodoItemViewModel(Guid itemId, string text, TodoItemStatus status)
    {
        ItemId = itemId;
        _text = text;
        _status = status;
        _isDone = status == TodoItemStatus.Done;
    }

    public Guid ItemId { get; private set; }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private TodoItemStatus _status;

    [ObservableProperty]
    private bool _isDone;

    /// <summary>Segoe Fluent Icons glyph used by the per-row indicator.</summary>
    public string GlyphForStatus => Status switch
    {
        TodoItemStatus.Pending => GlyphPending,
        TodoItemStatus.InProgress => GlyphInProgress,
        TodoItemStatus.Done => GlyphDone,
        TodoItemStatus.Skipped => GlyphSkipped,
        _ => GlyphPending
    };

    partial void OnIsDoneChanged(bool value)
    {
        Status = value ? TodoItemStatus.Done : TodoItemStatus.Pending;
        OnPropertyChanged(nameof(GlyphForStatus));
    }

    partial void OnStatusChanged(TodoItemStatus value)
    {
        OnPropertyChanged(nameof(GlyphForStatus));
        if (value == TodoItemStatus.Done && !IsDone)
        {
            IsDone = true;
        }
        else if (value != TodoItemStatus.Done && IsDone)
        {
            IsDone = false;
        }
    }
}
