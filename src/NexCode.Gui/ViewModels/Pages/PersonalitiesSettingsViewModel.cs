using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.PersonalitiesSettingsPage"/>. Spec §8.2 — personalities
/// stack on top of the active mode and can be scoped global or to a single project.
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

    [RelayCommand]
    private void AddPersonality()
    {
        var row = new PersonalityRowViewModel
        {
            Id = Guid.NewGuid(),
            Name = "New personality",
            Description = string.Empty,
            SystemPromptFragment = string.Empty,
            Tone = "neutral",
            Verbosity = "balanced",
            Scope = "global",
        };
        Personalities.Add(row);
        SelectedPersonality = row;
    }

    [RelayCommand]
    private void DuplicatePersonality(PersonalityRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var clone = new PersonalityRowViewModel
        {
            Id = Guid.NewGuid(),
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
    }

    [RelayCommand]
    private void DeletePersonality(PersonalityRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Personalities.Remove(row);
        if (SelectedPersonality == row)
        {
            SelectedPersonality = null;
        }
    }

    [RelayCommand]
    private void SetDefault(PersonalityRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var p in Personalities)
        {
            p.IsDefault = false;
        }

        row.IsDefault = true;
        StatusMessage = $"Default personality set to {row.Name} (wire-up pending).";
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
