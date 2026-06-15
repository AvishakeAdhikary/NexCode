using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.ModesSettingsPage"/>. Spec §7.1 — built-in modes are
/// locked from delete/rewrite while custom modes are fully editable. Editing happens through
/// the reusable <see cref="Gui.Pages.Settings.JsonEditorSheet"/>. List/upsert/delete are backed
/// by the helper over <c>mode.list/upsert/delete</c>.
/// </summary>
public sealed partial class ModesSettingsViewModel : ObservableObject
{
    public ObservableCollection<ModeRowViewModel> Modes { get; } = new();

    [ObservableProperty]
    private ModeRowViewModel? _selectedMode;

    [ObservableProperty]
    private string _statusMessage = "Modes load lazily; built-in entries are locked.";

    [ObservableProperty]
    private bool _isBusy;

    private HelperControlClient? _client;

    public bool CanEditSelected => SelectedMode is { IsBuiltIn: false };
    public bool CanDeleteSelected => SelectedMode is { IsBuiltIn: false };

    partial void OnSelectedModeChanged(ModeRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

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
            var response = await _client.ListModesAsync();
            ApplyListResponse(response);
            StatusMessage = $"Loaded {Modes.Count} mode(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load modes: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists the given row through <c>mode.upsert</c> and reloads.</summary>
    public async Task SaveAsync(ModeRowViewModel? row)
    {
        row ??= SelectedMode;
        if (row is null)
        {
            StatusMessage = "Select a mode to save.";
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
            await _client.UpsertModeAsync(row.ToUpsertRequest());
            await LoadAsync();
            StatusMessage = $"Saved {row.Name}.";
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
    private async Task AddMode()
    {
        var row = new ModeRowViewModel
        {
            Name = "New mode",
            SystemPrompt = "You are a helpful assistant.",
            IsBuiltIn = false,
            AllowedTools = Array.Empty<string>(),
        };
        Modes.Add(row);
        SelectedMode = row;
        await SaveAsync(row);
    }

    [RelayCommand]
    private async Task DuplicateMode(ModeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var clone = new ModeRowViewModel
        {
            Name = $"{row.Name} (copy)",
            SystemPrompt = row.SystemPrompt,
            Icon = row.Icon,
            AccentColor = row.AccentColor,
            IsBuiltIn = false,
            AllowedTools = (string[])row.AllowedTools.Clone(),
        };
        Modes.Add(clone);
        SelectedMode = clone;
        await SaveAsync(clone);
    }

    [RelayCommand]
    private async Task DeleteMode(ModeRowViewModel? row)
    {
        row ??= SelectedMode;
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        if (_client is null)
        {
            Modes.Remove(row);
            if (SelectedMode == row)
            {
                SelectedMode = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (row.Id != Guid.Empty)
            {
                await _client.DeleteModeAsync(row.Id);
            }
            await LoadAsync();
            StatusMessage = $"Deleted {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ImportMode() =>
        StatusMessage = "Paste mode JSON into the JSON editor and save to import it.";

    [RelayCommand]
    private void ExportMode(ModeRowViewModel? row) =>
        StatusMessage = row is null
            ? "Select a mode to export."
            : $"Export JSON for {row.Name}: open the JSON editor and copy the contents.";

    /// <summary>Applies a JSON-edited mode (from the editor sheet) and persists it.</summary>
    public async Task ApplyEditedJsonAsync(ModeRowViewModel row, string json)
    {
        try
        {
            var edited = JsonSerializer.Deserialize<ModeUpsertRequest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (edited is null)
            {
                StatusMessage = "Mode JSON was empty.";
                return;
            }

            row.Name = edited.Name;
            row.SystemPrompt = edited.SystemPrompt;
            row.Icon = edited.Icon;
            row.AccentColor = edited.AccentColor;
            row.AllowedTools = edited.AllowedTools ?? Array.Empty<string>();
        }
        catch (JsonException ex)
        {
            StatusMessage = $"Invalid mode JSON: {ex.Message}";
            return;
        }

        await SaveAsync(row);
    }

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
