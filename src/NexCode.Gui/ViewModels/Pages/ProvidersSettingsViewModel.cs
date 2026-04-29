using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.ProvidersSettingsPage"/>.
/// Spec §32 + §22 — list/add/edit/remove LLM providers and toggle the default. The IPC
/// wire-up is intentionally guarded so the panel renders even before the helper transport
/// is available; callers should resolve a real <see cref="Services.HelperControlClient"/>
/// once dependency injection lands.
/// </summary>
public sealed partial class ProvidersSettingsViewModel : ObservableObject
{
    public static string[] KnownProviderKeys { get; } =
    {
        "anthropic", "openai", "gemini", "bedrock", "azure-openai",
        "groq", "openrouter", "ollama", "lmstudio", "custom",
    };

    public ObservableCollection<ProviderRowViewModel> Providers { get; } = new();

    [ObservableProperty]
    private ProviderRowViewModel? _selectedProvider;

    [ObservableProperty]
    private string _statusMessage = "Provider list is loaded lazily on first activation.";

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private void AddProvider()
    {
        var row = new ProviderRowViewModel
        {
            ProviderKey = "custom",
            DisplayName = "New provider",
            BaseUrl = string.Empty,
            DefaultModelId = string.Empty,
            HasApiKey = false,
            IsDefault = false,
        };
        Providers.Add(row);
        SelectedProvider = row;
    }

    [RelayCommand]
    private void RemoveProvider(ProviderRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Providers.Remove(row);
        if (SelectedProvider == row)
        {
            SelectedProvider = null;
        }
    }

    [RelayCommand]
    private void SetDefault(ProviderRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var p in Providers)
        {
            p.IsDefault = false;
        }

        row.IsDefault = true;
        StatusMessage = $"Default provider set to {row.DisplayName} (wire-up pending).";
    }

    [RelayCommand]
    private void TestConnection(ProviderRowViewModel? row)
    {
        StatusMessage = row is null
            ? "Select a provider to test."
            : $"Testing {row.DisplayName}... (wire-up pending).";
    }

    /// <summary>Hydrates the panel from a <see cref="ProviderListResponse"/>.</summary>
    public void ApplyListResponse(ProviderListResponse response)
    {
        Providers.Clear();
        foreach (var p in response.Providers)
        {
            Providers.Add(ProviderRowViewModel.FromSummary(p, p.IsDefault));
        }
    }
}

/// <summary>Editable mirror of <see cref="ProviderSummary"/> + the API-key entry field.</summary>
public sealed partial class ProviderRowViewModel : ObservableObject
{
    [ObservableProperty] private string _providerKey = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _defaultModelId = string.Empty;
    [ObservableProperty] private string _apiKeyEntry = string.Empty;
    [ObservableProperty] private bool _hasApiKey;
    [ObservableProperty] private bool _isDefault;

    public ProviderUpsertRequest ToUpsertRequest() =>
        new(ProviderKey, DisplayName, BaseUrl, ApiKeyEntry, DefaultModelId, IsDefault);

    public static ProviderRowViewModel FromSummary(ProviderSummary s, bool isDefault) =>
        new()
        {
            ProviderKey = s.ProviderKey,
            DisplayName = s.DisplayName,
            BaseUrl = s.BaseUrl,
            DefaultModelId = s.DefaultModelId,
            HasApiKey = s.HasApiKey,
            IsDefault = isDefault,
        };
}
