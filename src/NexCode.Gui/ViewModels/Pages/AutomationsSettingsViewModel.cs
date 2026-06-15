using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.AutomationsSettingsPage"/> and
/// <see cref="Gui.Pages.AutomationsPage"/>. Spec §22.2 — list / upsert / run / delete / toggle
/// automations over the helper (<c>automation.list/upsert/run/delete/toggle</c>). The trigger
/// kind + cron expression are carried in <c>trigger_json</c>; the opaque step program lives in
/// <c>steps_json</c> and is round-tripped unchanged.
/// </summary>
public sealed partial class AutomationsSettingsViewModel : ObservableObject
{
    public ObservableCollection<AutomationRowViewModel> Automations { get; } = new();

    public string[] TriggerTypeOptions { get; } = { "manual", "cron", "file-watch", "hook" };

    [ObservableProperty] private AutomationRowViewModel? _selectedAutomation;
    [ObservableProperty] private string _statusMessage = "Automations load lazily on first activation.";
    [ObservableProperty] private bool _isBusy;

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
            var response = await _client.ListAutomationsAsync();
            ApplyListResponse(response);
            StatusMessage = $"Loaded {Automations.Count} automation(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load automations: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists the given row through <c>automation.upsert</c> and reloads.</summary>
    public async Task SaveAsync(AutomationRowViewModel row)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot save.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.UpsertAutomationAsync(row.ToUpsertRequest());
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
    private async Task AddAutomation()
    {
        var row = new AutomationRowViewModel
        {
            Name = "new-automation",
            TriggerSummary = "manual",
            StepsJson = "[]",
            IsEnabled = true,
        };
        Automations.Add(row);
        SelectedAutomation = row;
        await SaveAsync(row);
    }

    [RelayCommand]
    private async Task Run(AutomationRowViewModel? row)
    {
        row ??= SelectedAutomation;
        if (row is null)
        {
            StatusMessage = "Select an automation first.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot run.";
            return;
        }

        if (row.Id == Guid.Empty)
        {
            StatusMessage = "Save the automation before running it.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.RunAutomationAsync(row.Id);
            StatusMessage = $"Run requested for {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Toggle(AutomationRowViewModel? row)
    {
        row ??= SelectedAutomation;
        if (row is null)
        {
            return;
        }

        await SetEnabledAsync(row, !row.IsEnabled);
    }

    /// <summary>Persists the automation's enabled flag through <c>automation.toggle</c>.</summary>
    public async Task SetEnabledAsync(AutomationRowViewModel row, bool enabled)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot toggle.";
            return;
        }

        if (row.Id == Guid.Empty)
        {
            StatusMessage = "Save the automation before toggling it.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.ToggleAutomationAsync(row.Id, enabled);
            row.IsEnabled = enabled;
            StatusMessage = $"{row.Name} {(enabled ? "enabled" : "disabled")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Toggle failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Delete(AutomationRowViewModel? row)
    {
        row ??= SelectedAutomation;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Automations.Remove(row);
            if (SelectedAutomation == row)
            {
                SelectedAutomation = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (row.Id != Guid.Empty)
            {
                await _client.DeleteAutomationAsync(row.Id);
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

    public void ApplyListResponse(AutomationListResponse response)
    {
        Automations.Clear();
        foreach (var a in response.Automations)
        {
            Automations.Add(AutomationRowViewModel.FromSummary(a));
        }
    }
}

public sealed partial class AutomationRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _triggerSummary = "manual";
    [ObservableProperty] private string _stepsSummary = "0 steps";
    [ObservableProperty] private string _scheduleCron = string.Empty;
    [ObservableProperty] private string _stepsJson = "[]";
    [ObservableProperty] private bool _isEnabled = true;

    public AutomationUpsertRequest ToUpsertRequest() => new(
        Id == Guid.Empty ? null : Id,
        Name,
        BuildTriggerJson(),
        string.IsNullOrWhiteSpace(StepsJson) ? "[]" : StepsJson,
        IsEnabled);

    public static AutomationRowViewModel FromSummary(AutomationSummary s)
    {
        var (kind, cron) = ParseTrigger(s.TriggerJson);
        return new AutomationRowViewModel
        {
            Id = s.Id,
            Name = s.Name,
            TriggerSummary = kind,
            ScheduleCron = cron,
            StepsJson = string.IsNullOrWhiteSpace(s.StepsJson) ? "[]" : s.StepsJson,
            StepsSummary = SummarizeSteps(s.StepsJson),
            IsEnabled = s.Enabled,
        };
    }

    private string BuildTriggerJson()
    {
        var kind = string.IsNullOrWhiteSpace(TriggerSummary) ? "manual" : TriggerSummary;
        return JsonSerializer.Serialize(new { kind, cron = ScheduleCron ?? string.Empty });
    }

    private static (string Kind, string Cron) ParseTrigger(string? triggerJson)
    {
        if (string.IsNullOrWhiteSpace(triggerJson))
        {
            return ("manual", string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(triggerJson);
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "manual" : "manual";
            var cron = root.TryGetProperty("cron", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            return (kind, cron);
        }
        catch (JsonException)
        {
            return ("manual", string.Empty);
        }
    }

    private static string SummarizeSteps(string? stepsJson)
    {
        if (string.IsNullOrWhiteSpace(stepsJson))
        {
            return "0 steps";
        }

        try
        {
            using var doc = JsonDocument.Parse(stepsJson);
            var count = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
            return count == 1 ? "1 step" : $"{count} steps";
        }
        catch (JsonException)
        {
            return "0 steps";
        }
    }
}
