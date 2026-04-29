using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Controls;

public sealed partial class PermissionPromptCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(PermissionPromptViewModel),
        typeof(PermissionPromptCard),
        new PropertyMetadata(null));

    public PermissionPromptCard()
    {
        InitializeComponent();
    }

    public PermissionPromptViewModel? ViewModel
    {
        get => (PermissionPromptViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility GetLockVisibility(bool shown) =>
        shown ? Visibility.Visible : Visibility.Collapsed;
}
