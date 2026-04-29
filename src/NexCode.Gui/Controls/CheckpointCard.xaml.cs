using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Controls;

public sealed partial class CheckpointCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(CheckpointViewModel),
        typeof(CheckpointCard),
        new PropertyMetadata(null));

    public CheckpointCard()
    {
        InitializeComponent();
    }

    public CheckpointViewModel? ViewModel
    {
        get => (CheckpointViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
