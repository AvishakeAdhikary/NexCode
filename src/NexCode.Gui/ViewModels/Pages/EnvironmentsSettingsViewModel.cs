using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/* contract pending: EnvironmentListResponse / EnvironmentSummary not yet defined — stub below. */

/// <summary>VM for <see cref="Gui.Pages.Settings.EnvironmentsSettingsPage"/>.</summary>
public sealed partial class EnvironmentsSettingsViewModel : ObservableObject
{
    public ObservableCollection<EnvironmentRowViewModel> Environments { get; } = new();

    [ObservableProperty] private EnvironmentRowViewModel? _selectedEnvironment;
    [ObservableProperty] private string _statusMessage = "Environment list loads per project.";

    [RelayCommand]
    private void AddEnvironment()
    {
        var row = new EnvironmentRowViewModel
        {
            Id = Guid.NewGuid(),
            Name = "dev",
            DotEnvBody = string.Empty,
            IsActive = false,
        };
        Environments.Add(row);
        SelectedEnvironment = row;
    }

    [RelayCommand]
    private void SetActive(EnvironmentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var e in Environments)
        {
            e.IsActive = false;
        }

        row.IsActive = true;
        StatusMessage = $"Activated {row.Name} (wire-up pending).";
    }

    [RelayCommand]
    private void Delete(EnvironmentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Environments.Remove(row);
        if (SelectedEnvironment == row)
        {
            SelectedEnvironment = null;
        }
    }

    [RelayCommand]
    private void SyncToFile(EnvironmentRowViewModel? row) =>
        StatusMessage = row is null ? "Select an env first." : $"Sync {row.Name} to .env (wire-up pending).";
}

public sealed partial class EnvironmentRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dotEnvBody = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _maskValues = true;
}

/* Stub: pending wire-up. */
public sealed record EnvironmentListResponse(EnvironmentSummary[] Environments);
public sealed record EnvironmentSummary(Guid Id, string Name, string DotEnvBody, bool IsActive);
