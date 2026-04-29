using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NexCode.Gui.ViewModels;
using Windows.System;

namespace NexCode.Gui.Pages;

public sealed partial class ShellPage : Page
{
    public ShellPage()
    {
        InitializeComponent();
    }

    public ShellViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ShellViewModel vm)
        {
            BindViewModel(vm);
        }
        else if (DataContext is ShellViewModel inherited)
        {
            BindViewModel(inherited);
        }
    }

    public void BindViewModel(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.AttachDispatcher(DispatcherQueue);

        viewModel.PropertyChanged += OnShellPropertyChanged;
        SyncActiveSession();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Active))
        {
            DispatcherQueue.TryEnqueue(SyncActiveSession);
        }
    }

    private void SyncActiveSession()
    {
        if (ViewModel?.Active is null)
        {
            HeaderBar.Session = null;
            TranscriptRepeater.ItemsSource = null;
            UpdateComposerEnablement(active: null);
            return;
        }

        var active = ViewModel.Active;
        HeaderBar.Session = active;
        TranscriptRepeater.ItemsSource = active.Messages;
        UpdateComposerEnablement(active);

        if (active.Messages is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged -= OnMessagesChanged;
            ncc.CollectionChanged += OnMessagesChanged;
        }

        active.PropertyChanged -= OnActiveSessionPropertyChanged;
        active.PropertyChanged += OnActiveSessionPropertyChanged;
    }

    private void OnActiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.TurnInProgress))
        {
            DispatcherQueue.TryEnqueue(() => UpdateComposerEnablement(ViewModel?.Active));
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TranscriptScrollViewer.ChangeView(null, TranscriptScrollViewer.ScrollableHeight, null, disableAnimation: false);
            });
        }
    }

    private void UpdateComposerEnablement(SessionViewModel? active)
    {
        var canCompose = active is not null && active.CanCompose;
        ComposerBox.IsEnabled = canCompose;
        SendButton.IsEnabled = canCompose;
        StopButton.IsEnabled = active is not null && active.TurnInProgress;
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Active is null) return;
        ViewModel.Active.ComposerText = ComposerBox.Text;
        if (ViewModel.Active.SendMessageCommand.CanExecute(null))
        {
            ViewModel.Active.SendMessageCommand.Execute(null);
            ComposerBox.Text = string.Empty;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.Active?.StopCommand.Execute(null);
    }

    private void ComposerBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && IsCtrlDown())
        {
            Send_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private static bool IsCtrlDown()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }
}
