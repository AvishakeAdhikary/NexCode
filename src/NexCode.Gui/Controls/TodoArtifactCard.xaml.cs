using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NexCode.Gui.ViewModels;
using Windows.System;

namespace NexCode.Gui.Controls;

public sealed partial class TodoArtifactCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(TodoListViewModel),
        typeof(TodoArtifactCard),
        new PropertyMetadata(null));

    public event EventHandler? OpenInPlansRequested;

    public TodoArtifactCard()
    {
        InitializeComponent();
    }

    public TodoListViewModel? ViewModel
    {
        get => (TodoListViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void NewTaskBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddTask_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        var text = NewTaskBox.Text;
        if (ViewModel?.AddCommand.CanExecute(text) == true)
        {
            ViewModel.AddCommand.Execute(text);
            NewTaskBox.Text = string.Empty;
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.ShowAll = !ViewModel.ShowAll;
        ShowAllButton.Content = ViewModel.ShowAll ? "Collapse" : "Show all tasks";
    }

    private void OpenInPlans_Click(object sender, RoutedEventArgs e)
    {
        OpenInPlansRequested?.Invoke(this, EventArgs.Empty);
    }
}
