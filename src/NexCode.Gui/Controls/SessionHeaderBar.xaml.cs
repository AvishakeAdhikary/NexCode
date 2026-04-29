using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels;
using NexCode.Shared.Models;

namespace NexCode.Gui.Controls;

public sealed partial class SessionHeaderBar : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(SessionViewModel),
        typeof(SessionHeaderBar),
        new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty ProvidersProperty = DependencyProperty.Register(
        nameof(Providers),
        typeof(ProviderSettingsViewModel),
        typeof(SessionHeaderBar),
        new PropertyMetadata(null, OnProvidersChanged));

    public SessionHeaderBar()
    {
        InitializeComponent();
    }

    public SessionViewModel? Session
    {
        get => (SessionViewModel?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public ProviderSettingsViewModel? Providers
    {
        get => (ProviderSettingsViewModel?)GetValue(ProvidersProperty);
        set => SetValue(ProvidersProperty, value);
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SessionHeaderBar bar)
        {
            bar.SyncFromSession();
            if (e.OldValue is SessionViewModel oldVm)
            {
                oldVm.PropertyChanged -= bar.OnSessionPropertyChanged;
            }
            if (e.NewValue is SessionViewModel newVm)
            {
                newVm.PropertyChanged += bar.OnSessionPropertyChanged;
            }
        }
    }

    private static void OnProvidersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SessionHeaderBar bar)
        {
            bar.ModelSwitcher.ViewModel = bar.Providers;
        }
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionViewModel.Mode)
            or nameof(SessionViewModel.Personality)
            or nameof(SessionViewModel.ExecutionMode)
            or nameof(SessionViewModel.SandboxEnabled)
            or nameof(SessionViewModel.ShellName)
            or nameof(SessionViewModel.ModelDisplayName))
        {
            SyncFromSession();
        }
    }

    private void SyncFromSession()
    {
        if (Session is null) return;
        ModeText.Text = Session.Mode.ToString();
        PersonalityInitial.Text = string.IsNullOrEmpty(Session.Personality) ? "?" : Session.Personality[..1];
        SandboxToggle.IsOn = Session.SandboxEnabled;
        switch (Session.ExecutionMode)
        {
            case ExecutionMode.Local: LocalRadio.IsChecked = true; break;
            case ExecutionMode.Remote: RemoteRadio.IsChecked = true; break;
            case ExecutionMode.Cloud: CloudRadio.IsChecked = true; break;
        }
        ShellCombo.SelectedIndex = Session.ShellName switch
        {
            "bash" => 1,
            "cmd" => 2,
            _ => 0
        };
    }

    private void ExecutionMode_Checked(object sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        if (sender is RadioButton { IsChecked: true, Tag: string mode } && Enum.TryParse<ExecutionMode>(mode, out var em))
        {
            Session.ExecutionMode = em;
        }
    }

    private void Sandbox_Toggled(object sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        Session.SandboxEnabled = SandboxToggle.IsOn;
    }

    private void ShellCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Session is null) return;
        Session.ShellName = ShellCombo.SelectedIndex switch
        {
            1 => "bash",
            2 => "cmd",
            _ => "pwsh"
        };
    }
}
