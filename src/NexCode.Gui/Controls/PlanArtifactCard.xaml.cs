using Markdig;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using NexCode.Gui.ViewModels;
using NexCode.Shared.Models;

namespace NexCode.Gui.Controls;

public sealed partial class PlanArtifactCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(PlanViewModel),
        typeof(PlanArtifactCard),
        new PropertyMetadata(null, OnViewModelChanged));

    public static readonly DependencyProperty RejectExpandedProperty = DependencyProperty.Register(
        nameof(RejectExpanded),
        typeof(Visibility),
        typeof(PlanArtifactCard),
        new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty ChangeRequestExpandedProperty = DependencyProperty.Register(
        nameof(ChangeRequestExpanded),
        typeof(Visibility),
        typeof(PlanArtifactCard),
        new PropertyMetadata(Visibility.Collapsed));

    private bool _showFull;

    public PlanArtifactCard()
    {
        InitializeComponent();
    }

    public PlanViewModel? ViewModel
    {
        get => (PlanViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility RejectExpanded
    {
        get => (Visibility)GetValue(RejectExpandedProperty);
        set => SetValue(RejectExpandedProperty, value);
    }

    public Visibility ChangeRequestExpanded
    {
        get => (Visibility)GetValue(ChangeRequestExpandedProperty);
        set => SetValue(ChangeRequestExpandedProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlanArtifactCard card)
        {
            card.RenderMarkdown();
            if (e.OldValue is PlanViewModel oldVm)
            {
                oldVm.PropertyChanged -= card.OnViewModelPropertyChanged;
            }
            if (e.NewValue is PlanViewModel newVm)
            {
                newVm.PropertyChanged += card.OnViewModelPropertyChanged;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlanViewModel.Content))
        {
            RenderMarkdown();
        }
    }

    private void RenderMarkdown()
    {
        if (ContentRichTextBlock is null) return;
        ContentRichTextBlock.Blocks.Clear();
        if (ViewModel is null) return;

        var preview = _showFull ? ViewModel.Content : ViewModel.ContentPreview;
        // Markdig markdown -> plaintext (rich rendering deferred to slice 0014).
        // We still strip basic markdown so the preview reads cleanly.
        var plain = Markdown.ToPlainText(preview);
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = plain });
        ContentRichTextBlock.Blocks.Add(paragraph);
    }

    private void ShowFull_Click(object sender, RoutedEventArgs e)
    {
        _showFull = !_showFull;
        ShowFullLabel.Text = _showFull ? "Show preview" : "Show full plan";
        RenderMarkdown();
    }

    private void ToggleReject_Click(object sender, RoutedEventArgs e)
    {
        RejectExpanded = RejectExpanded == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ChangeRequestExpanded = Visibility.Collapsed;
    }

    private void ToggleChangeRequest_Click(object sender, RoutedEventArgs e)
    {
        ChangeRequestExpanded = ChangeRequestExpanded == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        RejectExpanded = Visibility.Collapsed;
    }

    public string GetStatusLabel(PlanStatus status) => status.ToString();

    public Visibility GetActionVisibility(PlanStatus status) =>
        status == PlanStatus.PendingConfirmation ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetCollapseToggleVisibility(string content) =>
        !string.IsNullOrEmpty(content) && content.Length > 160 ? Visibility.Visible : Visibility.Collapsed;

    public string GetVersionLabel(int version) => $"v{version}";

    public Brush GetStatusBrush(PlanStatus status)
    {
        var key = status switch
        {
            PlanStatus.Draft => "NexCode.Text.Tertiary",
            PlanStatus.PendingConfirmation => "NexCode.Status.Warning",
            PlanStatus.Confirmed => "NexCode.Status.Success",
            PlanStatus.Rejected => "NexCode.Status.Error",
            PlanStatus.Completed => "NexCode.Status.Success",
            PlanStatus.Deleted => "NexCode.Text.Tertiary",
            _ => "NexCode.Text.Tertiary"
        };
        if (Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush b)
        {
            return b;
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
