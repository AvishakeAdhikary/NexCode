using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.MemorySettingsPage"/>. Spec §9 — toggles for global vs
/// project memory injection, the top-N retrieval count, max stored memories, and the eviction
/// policy. Actual memory CRUD lives on the dedicated <see cref="Gui.Pages.MemoriesPage"/>.
/// </summary>
public sealed partial class MemorySettingsViewModel : ObservableObject
{
    public string[] EvictionPolicies { get; } = { "lru", "lfu", "fifo", "manual" };

    [ObservableProperty] private bool _enableGlobalInjection = true;
    [ObservableProperty] private bool _enableProjectInjection = true;
    [ObservableProperty] private bool _enableSessionScratchpad = true;
    [ObservableProperty] private int _topNInjection = 8;
    [ObservableProperty] private int _maxMemories = 1000;
    [ObservableProperty] private string _evictionPolicy = "lru";
    [ObservableProperty] private string _statusMessage = "Settings persist via theme/keybinding helper IPC.";

    [RelayCommand]
    private void Export() => StatusMessage = "Memory export started (wire-up pending).";

    [RelayCommand]
    private void Import() => StatusMessage = "Memory import started (wire-up pending).";

    [RelayCommand]
    private void ResetDefaults()
    {
        EnableGlobalInjection = true;
        EnableProjectInjection = true;
        EnableSessionScratchpad = true;
        TopNInjection = 8;
        MaxMemories = 1000;
        EvictionPolicy = "lru";
        StatusMessage = "Memory settings reset to defaults.";
    }
}
