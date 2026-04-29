using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// View model for provider configuration / model picker. Backs the
/// <c>ModelSwitcherDropdown</c> and the dedicated provider settings sheet.
/// </summary>
public sealed partial class ProviderSettingsViewModel : ObservableViewModelBase
{
    public ProviderSettingsViewModel()
    {
        Providers = [];
    }

    public ObservableCollection<ProviderEntryViewModel> Providers { get; }

    [ObservableProperty]
    private ProviderEntryViewModel? _defaultProvider;

    [ObservableProperty]
    private string? _searchText;

    public IEnumerable<ProviderEntryViewModel> FilteredProviders =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Providers
            : Providers.Where(p =>
                p.DisplayName.Contains(SearchText!, StringComparison.OrdinalIgnoreCase) ||
                p.DefaultModelId.Contains(SearchText!, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string? value) => OnPropertyChanged(nameof(FilteredProviders));

    public event EventHandler<ProviderUpsertEvent>? UpsertRequested;
    public event EventHandler<string>? RemoveRequested;
    public event EventHandler<string>? SetDefaultRequested;

    [RelayCommand]
    private void Upsert(ProviderEntryViewModel? entry)
    {
        if (entry is null) return;
        UpsertRequested?.Invoke(this, new ProviderUpsertEvent(entry));
    }

    [RelayCommand]
    private void Remove(ProviderEntryViewModel? entry)
    {
        if (entry is null) return;
        Providers.Remove(entry);
        RemoveRequested?.Invoke(this, entry.ProviderKey);
    }

    [RelayCommand]
    private void SetDefault(ProviderEntryViewModel? entry)
    {
        if (entry is null) return;
        foreach (var p in Providers)
        {
            p.IsDefault = ReferenceEquals(p, entry);
        }
        DefaultProvider = entry;
        SetDefaultRequested?.Invoke(this, entry.ProviderKey);
    }
}

public sealed partial class ProviderEntryViewModel : ObservableViewModelBase
{
    public ProviderEntryViewModel(
        string providerKey,
        string displayName,
        string baseUrl,
        string defaultModelId,
        bool hasApiKey,
        bool isDefault)
    {
        ProviderKey = providerKey;
        _displayName = displayName;
        _baseUrl = baseUrl;
        _defaultModelId = defaultModelId;
        _hasApiKey = hasApiKey;
        _isDefault = isDefault;
    }

    public string ProviderKey { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _baseUrl;

    [ObservableProperty]
    private string _defaultModelId;

    [ObservableProperty]
    private string? _pendingApiKey;

    [ObservableProperty]
    private bool _hasApiKey;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private bool _supportsVision;

    [ObservableProperty]
    private bool _supportsTools;

    public Microsoft.UI.Xaml.Visibility GetVisionVisibility(bool supportsVision) =>
        supportsVision ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility GetToolsVisibility(bool supportsTools) =>
        supportsTools ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility GetDefaultVisibility(bool isDefault) =>
        isDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

public sealed record ProviderUpsertEvent(ProviderEntryViewModel Entry);
