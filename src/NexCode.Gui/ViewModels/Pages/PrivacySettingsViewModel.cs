using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.PrivacySettingsPage"/>. Spec §30/§31 — telemetry is
/// opt-in, queue size visible, and the queue clearable. Consent and queue size are loaded from
/// the helper on activation; flipping the toggle calls <c>telemetry.consent</c> and Clear calls
/// <c>telemetry.clear</c>.
/// </summary>
public sealed partial class PrivacySettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _telemetryEnabled;
    [ObservableProperty] private int _queueSize;
    [ObservableProperty] private string _samplePayloadJson = "{ /* no sample yet */ }";
    [ObservableProperty] private string _statusMessage = "Telemetry is off by default.";
    [ObservableProperty] private bool _isBusy;

    private HelperControlClient? _client;

    // Set while the toggle is being populated from the helper so the resulting property change
    // does not echo back as a fresh consent write.
    private bool _suppressConsentWrite;

    /// <summary>Called by the page on activation: binds the helper transport and loads consent + queue size.</summary>
    public async Task InitializeAsync(HelperControlClient client)
    {
        _client = client;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var consent = await _client.GetTelemetryConsentAsync();
            _suppressConsentWrite = true;
            TelemetryEnabled = consent.Enabled;
            _suppressConsentWrite = false;
            QueueSize = consent.QueuedEvents;
            StatusMessage = consent.Enabled
                ? $"Telemetry enabled. {QueueSize} event(s) queued."
                : "Telemetry is off.";
        }
        catch (Exception ex)
        {
            _suppressConsentWrite = false;
            StatusMessage = $"Could not load telemetry settings: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnTelemetryEnabledChanged(bool value)
    {
        if (_suppressConsentWrite || _client is null)
        {
            return;
        }

        _ = SetConsentAsync(value);
    }

    private async Task SetConsentAsync(bool enabled)
    {
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var consent = await _client.SetTelemetryConsentAsync(enabled);
            QueueSize = consent.QueuedEvents;
            StatusMessage = consent.Enabled
                ? "Telemetry enabled."
                : "Telemetry disabled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not update telemetry consent: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ViewSamplePayload() =>
        StatusMessage = "Telemetry events are collected only while consent is enabled.";

    [RelayCommand]
    private async Task ClearQueue()
    {
        if (_client is null)
        {
            QueueSize = 0;
            StatusMessage = "Helper unavailable; nothing cleared.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _client.ClearTelemetryAsync();
            QueueSize = 0;
            StatusMessage = $"Cleared {result.Cleared} queued telemetry event(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clear failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportGdprArchive() =>
        StatusMessage = "Use the History panel's export to download your stored data.";

    [RelayCommand]
    private void RequestAccountDeletion() =>
        StatusMessage = "Account deletion is handled from your account portal; sign out to disconnect this device.";
}
