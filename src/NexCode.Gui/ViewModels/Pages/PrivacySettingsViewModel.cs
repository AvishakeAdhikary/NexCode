using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.PrivacySettingsPage"/>. Spec §31 — telemetry is opt-in,
/// queue size visible, sample payloads inspectable, and GDPR export / deletion exposed.
/// </summary>
public sealed partial class PrivacySettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _telemetryEnabled;
    [ObservableProperty] private int _queueSize;
    [ObservableProperty] private string _samplePayloadJson = "{ /* no sample yet */ }";
    [ObservableProperty] private string _statusMessage = "Telemetry is off by default.";

    [RelayCommand]
    private void ViewSamplePayload() => StatusMessage = "Sample payload refreshed (wire-up pending).";

    [RelayCommand]
    private void ClearQueue()
    {
        QueueSize = 0;
        StatusMessage = "Telemetry queue cleared (wire-up pending).";
    }

    [RelayCommand]
    private void ExportGdprArchive() => StatusMessage = "GDPR export requested (wire-up pending).";

    [RelayCommand]
    private void RequestAccountDeletion() =>
        StatusMessage = "Account deletion request submitted (wire-up pending — confirmation required).";
}
