using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexCode.Gui.ViewModels.Pages;

/* contract pending: Slice 0017 AutomationListResponse not yet shipped — local stub at bottom. */

/// <summary>VM for <see cref="Gui.Pages.Settings.AutomationsSettingsPage"/>.</summary>
public sealed partial class AutomationsSettingsViewModel : ObservableObject
{
    public ObservableCollection<AutomationRowViewModel> Automations { get; } = new();

    [ObservableProperty] private AutomationRowViewModel? _selectedAutomation;
    [ObservableProperty] private string _statusMessage = "Automation list (wire-up pending).";

    [RelayCommand]
    private void AddAutomation()
    {
        var row = new AutomationRowViewModel
        {
            Id = Guid.NewGuid(),
            Name = "new-automation",
            TriggerSummary = "manual",
            StepsSummary = "0 step",
            IsEnabled = true,
        };
        Automations.Add(row);
        SelectedAutomation = row;
    }

    [RelayCommand]
    private void Run(AutomationRowViewModel? row) =>
        StatusMessage = row is null ? "Select an automation first." : $"Running {row.Name} (wire-up pending).";

    [RelayCommand]
    private void Toggle(AutomationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsEnabled = !row.IsEnabled;
        StatusMessage = $"{row.Name} {(row.IsEnabled ? "enabled" : "disabled")}.";
    }

    [RelayCommand]
    private void Delete(AutomationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Automations.Remove(row);
        if (SelectedAutomation == row)
        {
            SelectedAutomation = null;
        }
    }
}

public sealed partial class AutomationRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _triggerSummary = string.Empty;
    [ObservableProperty] private string _stepsSummary = string.Empty;
    [ObservableProperty] private string _scheduleCron = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
}

/* Stub: pending wire-up. */
public sealed record AutomationListResponse(AutomationSummary[] Automations);
public sealed record AutomationSummary(
    Guid Id, string Name, string TriggerSummary, string StepsSummary, string ScheduleCron, bool IsEnabled);
