using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.ModesSettingsPage"/>. Spec §7.1 — built-in modes are
/// locked from delete/rewrite while custom modes are fully editable. Editing happens through
/// the reusable <see cref="Gui.Pages.Settings.JsonEditorSheet"/>.
/// </summary>
public sealed partial class ModesSettingsViewModel : ObservableObject
{
    public ObservableCollection<ModeRowViewModel> Modes { get; } = new();

    [ObservableProperty]
    private ModeRowViewModel? _selectedMode;

    [ObservableProperty]
    private string _statusMessage = "Modes load lazily; built-in entries are locked.";

    public bool CanEditSelected => SelectedMode is { IsBuiltIn: false };
    public bool CanDeleteSelected => SelectedMode is { IsBuiltIn: false };

    partial void OnSelectedModeChanged(ModeRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    [RelayCommand]
    private void AddMode()
    {
        var row = new ModeRowViewModel
        {
            Id = Guid.NewGuid(),
            Name = "New mode",
            SystemPrompt = "You are a helpful assistant.",
            IsBuiltIn = false,
            AllowedTools = Array.Empty<string>(),
        };
        Modes.Add(row);
        SelectedMode = row;
    }

    [RelayCommand]
    private void DuplicateMode(ModeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var clone = new ModeRowViewModel
        {
            Id = Guid.NewGuid(),
            Name = $"{row.Name} (copy)",
            SystemPrompt = row.SystemPrompt,
            Icon = row.Icon,
            AccentColor = row.AccentColor,
            IsBuiltIn = false,
            AllowedTools = (string[])row.AllowedTools.Clone(),
        };
        Modes.Add(clone);
        SelectedMode = clone;
    }

    [RelayCommand]
    private void DeleteMode(ModeRowViewModel? row)
    {
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        Modes.Remove(row);
        if (SelectedMode == row)
        {
            SelectedMode = null;
        }
    }

    [RelayCommand]
    private void ImportMode() => StatusMessage = "Import mode JSON (wire-up pending).";

    [RelayCommand]
    private void ExportMode(ModeRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select a mode to export."
            : $"Export {row.Name} (wire-up pending).";

    public void ApplyListResponse(ModeListResponse response)
    {
        Modes.Clear();
        foreach (var m in response.Modes)
        {
            Modes.Add(ModeRowViewModel.FromSummary(m));
        }
    }
}

public sealed partial class ModeRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _systemPrompt = string.Empty;
    [ObservableProperty] private string? _icon;
    [ObservableProperty] private string? _accentColor;
    [ObservableProperty] private bool _isBuiltIn;
    [ObservableProperty] private string[] _allowedTools = Array.Empty<string>();

    public ModeUpsertRequest ToUpsertRequest() =>
        new(Id == Guid.Empty ? null : Id, Name, SystemPrompt, Icon, AccentColor, AllowedTools);

    public static ModeRowViewModel FromSummary(ModeSummary s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        SystemPrompt = s.SystemPrompt,
        Icon = s.Icon,
        AccentColor = s.AccentColor,
        IsBuiltIn = s.IsBuiltIn,
        AllowedTools = s.AllowedTools,
    };
}
