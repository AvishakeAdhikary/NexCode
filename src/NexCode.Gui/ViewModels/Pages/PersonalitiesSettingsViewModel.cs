using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.PersonalitiesSettingsPage"/>. Spec §8.2 — personalities
/// stack on top of the active mode and can be scoped global or to a single project. List, upsert
/// and delete are backed by the helper over <c>personality.list/upsert/delete</c>.
/// </summary>
public sealed partial class PersonalitiesSettingsViewModel : ObservableObject
{
    public ObservableCollection<PersonalityRowViewModel> Personalities { get; } = new();

    public string[] ToneOptions { get; } = { "neutral", "warm", "concise", "playful", "formal" };

    public string[] VerbosityOptions { get; } = { "terse", "balanced", "verbose" };

    public string[] ScopeOptions { get; } = { "global", "project" };

    [ObservableProperty]
    private PersonalityRowViewModel? _selectedPersonality;

    [ObservableProperty]
    private string _statusMessage = "Personalities load lazily on first activation.";

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
            var response = await _client.ListPersonalitiesAsync();
            ApplyListResponse(response);
            StatusMessage = $"Loaded {Personalities.Count} personality(ies).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load personalities: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists the given row through <c>personality.upsert</c> and reloads.</summary>
    private async Task SaveAsync(PersonalityRowViewModel row)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot save.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.UpsertPersonalityAsync(row.ToUpsertRequest());
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
    private async Task AddPersonality()
    {
        var row = new PersonalityRowViewModel
        {
            Name = "New personality",
            Description = string.Empty,
            SystemPromptFragment = string.Empty,
            Tone = "neutral",
            Verbosity = "balanced",
            Scope = "global",
        };
        Personalities.Add(row);
        SelectedPersonality = row;
        await SaveAsync(row);
    }

    [RelayCommand]
    private async Task DuplicatePersonality(PersonalityRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var clone = new PersonalityRowViewModel
        {
            Name = $"{row.Name} (copy)",
            Description = row.Description,
            SystemPromptFragment = row.SystemPromptFragment,
            Tone = row.Tone,
            Verbosity = row.Verbosity,
            Scope = row.Scope,
            ProjectId = row.ProjectId,
            IsDefault = false,
        };
        Personalities.Add(clone);
        SelectedPersonality = clone;
        await SaveAsync(clone);
    }

    [RelayCommand]
    private async Task DeletePersonality(PersonalityRowViewModel? row)
    {
        row ??= SelectedPersonality;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Personalities.Remove(row);
            if (SelectedPersonality == row)
            {
                SelectedPersonality = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (row.Id != Guid.Empty)
            {
                await _client.DeletePersonalityAsync(row.Id);
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
    private async Task SetDefault(PersonalityRowViewModel? row)
    {
        row ??= SelectedPersonality;
        if (row is null)
        {
            return;
        }

        foreach (var p in Personalities)
        {
            p.IsDefault = false;
        }

        row.IsDefault = true;
        await SaveAsync(row);
        StatusMessage = $"Default personality set to {row.Name}.";
    }

    public void ApplyListResponse(PersonalityListResponse response)
    {
        Personalities.Clear();
        foreach (var p in response.Personalities)
        {
            Personalities.Add(PersonalityRowViewModel.FromSummary(p));
        }
    }
}

public sealed partial class PersonalityRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _systemPromptFragment = string.Empty;
    [ObservableProperty] private string _tone = "neutral";
    [ObservableProperty] private string _verbosity = "balanced";
    [ObservableProperty] private string _scope = "global";
    [ObservableProperty] private Guid? _projectId;
    [ObservableProperty] private bool _isDefault;

    public PersonalityUpsertRequest ToUpsertRequest() => new(
        Id == Guid.Empty ? null : Id,
        Name,
        Description,
        SystemPromptFragment,
        Tone,
        Verbosity,
        Scope,
        ProjectId,
        IsDefault);

    public static PersonalityRowViewModel FromSummary(PersonalitySummary s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        SystemPromptFragment = s.SystemPromptFragment,
        Tone = s.Tone,
        Verbosity = s.Verbosity,
        Scope = s.Scope,
        ProjectId = s.ProjectId,
        IsDefault = s.IsDefault,
    };
}
