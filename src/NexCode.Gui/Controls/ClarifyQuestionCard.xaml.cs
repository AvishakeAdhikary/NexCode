using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Controls;

public sealed partial class ClarifyQuestionCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(ClarifyQuestionViewModel),
        typeof(ClarifyQuestionCard),
        new PropertyMetadata(null));

    public ClarifyQuestionCard()
    {
        InitializeComponent();
    }

    public ClarifyQuestionViewModel? ViewModel
    {
        get => (ClarifyQuestionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void ToggleCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClarifyQuestionItemVM item })
        {
            item.CustomExpanded = !item.CustomExpanded;
        }
    }
}
