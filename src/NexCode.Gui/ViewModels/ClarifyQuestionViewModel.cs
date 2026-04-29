using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// MCQ-style clarify question card view model (spec §13).
/// </summary>
public sealed partial class ClarifyQuestionViewModel : ObservableViewModelBase
{
    public ClarifyQuestionViewModel()
        : this(Guid.Empty, string.Empty)
    {
    }

    public ClarifyQuestionViewModel(Guid questionId, string context)
    {
        QuestionId = questionId;
        _context = context;
        Questions = [];
    }

    public Guid QuestionId { get; private set; }

    [ObservableProperty]
    private string _context;

    public ObservableCollection<ClarifyQuestionItemVM> Questions { get; }

    public bool CanSubmit => Questions.All(q => !q.Required || q.HasAnswer);

    public event EventHandler<ClarifySubmission>? SubmitRequested;
    public event EventHandler? SkipAllRequested;

    public void RaiseCanSubmitChanged() => OnPropertyChanged(nameof(CanSubmit));

    [RelayCommand]
    private void Submit()
    {
        var answers = Questions.Select(q => q.ToAnswer()).ToArray();
        SubmitRequested?.Invoke(this, new ClarifySubmission(QuestionId, answers));
    }

    [RelayCommand]
    private void SkipAll()
    {
        SkipAllRequested?.Invoke(this, EventArgs.Empty);
    }
}

public sealed partial class ClarifyQuestionItemVM : ObservableViewModelBase
{
    public ClarifyQuestionItemVM(string id, string prompt, bool allowMultiSelect, bool allowCustom, bool required)
    {
        Id = id;
        _prompt = prompt;
        AllowMultiSelect = allowMultiSelect;
        AllowCustom = allowCustom;
        Required = required;
        Options = [];
        Options.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAnswer));
    }

    public string Id { get; }

    public bool AllowMultiSelect { get; }

    public bool AllowCustom { get; }

    public bool Required { get; }

    [ObservableProperty]
    private string _prompt;

    [ObservableProperty]
    private string? _customText;

    [ObservableProperty]
    private bool _customExpanded;

    public ObservableCollection<ClarifyOptionItemVM> Options { get; }

    public bool HasAnswer =>
        Options.Any(o => o.IsSelected) || !string.IsNullOrWhiteSpace(CustomText);

    public Microsoft.UI.Xaml.Visibility GetCustomToggleVisibility(bool allowCustom) =>
        allowCustom ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility GetExpandedVisibility(bool expanded) =>
        expanded ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnCustomTextChanged(string? value) => OnPropertyChanged(nameof(HasAnswer));

    public ClarifyAnswerItem ToAnswer()
    {
        var selected = Options.Where(o => o.IsSelected).Select(o => o.Id).ToArray();
        return new ClarifyAnswerItem(Id, selected, CustomText);
    }
}

public sealed partial class ClarifyOptionItemVM : ObservableViewModelBase
{
    public ClarifyOptionItemVM(string id, string label)
    {
        Id = id;
        _label = label;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed record ClarifySubmission(Guid QuestionId, ClarifyAnswerItem[] Answers);
