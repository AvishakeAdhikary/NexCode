using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
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

    private HelperControlClient? _client;

    /// <summary>Called by the page on activation: binds the helper transport and loads the list.</summary>
    public async Task InitializeAsync(HelperControlClient client)
    {
        _client = client;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Reload() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var response = await _client.ListProvidersAsync();
            ApplyListResponse(response);
            StatusMessage = Providers.Count == 0
                ? "No providers configured yet. Add one and save to start chatting."
                : $"Loaded {Providers.Count} provider(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load providers: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

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
        StatusMessage = "Fill in the details and click Save to persist this provider.";
    }

    [RelayCommand]
    private async Task SaveProvider(ProviderRowViewModel? row)
    {
        row ??= SelectedProvider;
        if (row is null)
        {
            StatusMessage = "Select a provider to save.";
            return;
        }

        if (string.IsNullOrWhiteSpace(row.ProviderKey))
        {
            StatusMessage = "A provider type is required.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot save.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.UpsertProviderAsync(row.ToUpsertRequest());
            await LoadAsync();
            StatusMessage = $"Saved {row.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveProvider(ProviderRowViewModel? row)
    {
        row ??= SelectedProvider;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Providers.Remove(row);
            if (SelectedProvider == row)
            {
                SelectedProvider = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(row.ProviderKey))
            {
                await _client.RemoveProviderAsync(row.ProviderKey);
            }
            await LoadAsync();
            StatusMessage = $"Removed {row.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetDefault(ProviderRowViewModel? row)
    {
        row ??= SelectedProvider;
        if (row is null || string.IsNullOrWhiteSpace(row.ProviderKey))
        {
            return;
        }

        if (_client is null)
        {
            foreach (var p in Providers)
            {
                p.IsDefault = false;
            }
            row.IsDefault = true;
            return;
        }

        IsBusy = true;
        try
        {
            await _client.SetDefaultProviderAsync(row.ProviderKey);
            await LoadAsync();
            StatusMessage = $"Default provider set to {row.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Set default failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TestConnection(ProviderRowViewModel? row)
    {
        // No dedicated provider-test IPC endpoint exists yet; validation happens when a
        // session is started against the provider. Keep the message honest.
        StatusMessage = row is null
            ? "Select a provider to test."
            : $"Save {row.DisplayName}, then start a session to validate the connection.";
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
