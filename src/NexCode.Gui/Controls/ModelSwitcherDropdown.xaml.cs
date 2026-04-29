using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Controls;

public sealed partial class ModelSwitcherDropdown : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(ProviderSettingsViewModel),
        typeof(ModelSwitcherDropdown),
        new PropertyMetadata(null, OnViewModelChanged));

    public event EventHandler<ProviderEntryViewModel>? ProviderSelected;
    public event EventHandler? OpenProviderSettingsRequested;

    public ModelSwitcherDropdown()
    {
        InitializeComponent();
    }

    public ProviderSettingsViewModel? ViewModel
    {
        get => (ProviderSettingsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelSwitcherDropdown self)
        {
            self.RefreshList();
        }
    }

    private void RefreshList()
    {
        if (ViewModel is null) return;
        ProviderList.ItemsSource = ViewModel.FilteredProviders.ToList();
        var defaultName = ViewModel.DefaultProvider?.DisplayName;
        if (!string.IsNullOrEmpty(defaultName))
        {
            ModelLabel.Text = defaultName;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.SearchText = SearchBox.Text;
        RefreshList();
    }

    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderList.SelectedItem is ProviderEntryViewModel entry)
        {
            ModelLabel.Text = entry.DisplayName;
            ProviderSelected?.Invoke(this, entry);
            DropFlyout.Hide();
        }
    }

    private void OpenProviderSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenProviderSettingsRequested?.Invoke(this, EventArgs.Empty);
        DropFlyout.Hide();
    }
}
