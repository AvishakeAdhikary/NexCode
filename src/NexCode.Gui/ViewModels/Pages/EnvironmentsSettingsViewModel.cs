using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.Settings.EnvironmentsSettingsPage"/>. Spec §31 — per-project
/// .env-style variable bundles. List / upsert / delete are backed by the helper over
/// <c>environment.list/upsert/delete</c>. Variable values are not echoed back by the helper for
/// privacy: a reloaded bundle shows its keys (values blank) until re-entered.
/// </summary>
public sealed partial class EnvironmentsSettingsViewModel : ObservableObject
{
    public ObservableCollection<EnvironmentRowViewModel> Environments { get; } = new();

    [ObservableProperty] private EnvironmentRowViewModel? _selectedEnvironment;
    [ObservableProperty] private string _statusMessage = "Environments load lazily on first activation.";
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
            var response = await _client.ListEnvironmentsAsync();
            ApplyListResponse(response);
            StatusMessage = $"Loaded {Environments.Count} environment(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load environments: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists the given row through <c>environment.upsert</c> and reloads.</summary>
    public async Task SaveAsync(EnvironmentRowViewModel row)
    {
        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot save.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.UpsertEnvironmentAsync(row.ToUpsertRequest());
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
    private async Task AddEnvironment()
    {
        var row = new EnvironmentRowViewModel
        {
            Name = "dev",
            DotEnvBody = string.Empty,
            IsActive = false,
        };
        Environments.Add(row);
        SelectedEnvironment = row;
        await SaveAsync(row);
    }

    [RelayCommand]
    private async Task SetActive(EnvironmentRowViewModel? row)
    {
        row ??= SelectedEnvironment;
        if (row is null)
        {
            return;
        }

        foreach (var e in Environments)
        {
            e.IsActive = false;
        }

        row.IsActive = true;
        await SaveAsync(row);
        StatusMessage = $"Activated {row.Name}.";
    }

    [RelayCommand]
    private async Task Delete(EnvironmentRowViewModel? row)
    {
        row ??= SelectedEnvironment;
        if (row is null)
        {
            return;
        }

        if (_client is null)
        {
            Environments.Remove(row);
            if (SelectedEnvironment == row)
            {
                SelectedEnvironment = null;
            }
            return;
        }

        IsBusy = true;
        try
        {
            if (row.Id != Guid.Empty)
            {
                await _client.DeleteEnvironmentAsync(row.Id);
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
    private async Task SyncToFile(EnvironmentRowViewModel? row)
    {
        // Persisting the bundle is the durable "sync"; writing a literal .env file to disk has no
        // dedicated IPC endpoint, so the helper-stored bundle is the source of truth.
        row ??= SelectedEnvironment;
        if (row is null)
        {
            StatusMessage = "Select an environment first.";
            return;
        }

        await SaveAsync(row);
        StatusMessage = $"Synced {row.Name} to the helper-stored bundle.";
    }

    public void ApplyListResponse(EnvironmentListResponse response)
    {
        Environments.Clear();
        foreach (var e in response.Environments)
        {
            Environments.Add(EnvironmentRowViewModel.FromSummary(e));
        }
    }
}

public sealed partial class EnvironmentRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private Guid _projectId;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dotEnvBody = string.Empty;
    [ObservableProperty] private string? _overridesJson;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _maskValues = true;

    public EnvironmentUpsertRequest ToUpsertRequest() => new(
        Id == Guid.Empty ? null : Id,
        ProjectId,
        Name,
        ParseDotEnv(DotEnvBody),
        OverridesJson);

    public static EnvironmentRowViewModel FromSummary(EnvironmentSummary s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        Name = s.Name,
        DotEnvBody = BuildDotEnv(s.VariableKeys),
        OverridesJson = s.OverridesJson,
    };

    /// <summary>Parses a .env body into key/value pairs, skipping blanks and <c>#</c> comments.</summary>
    private static EnvironmentVariablePair[] ParseDotEnv(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<EnvironmentVariablePair>();
        }

        var pairs = new List<EnvironmentVariablePair>();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0)
            {
                pairs.Add(new EnvironmentVariablePair(key, value));
            }
        }

        return pairs.ToArray();
    }

    /// <summary>
    /// Rebuilds an editable .env body from the keys the helper returns. Values are intentionally
    /// blank: the helper does not echo secret values back to the GUI.
    /// </summary>
    private static string BuildDotEnv(string[] variableKeys)
    {
        if (variableKeys is null || variableKeys.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var key in variableKeys)
        {
            builder.Append(key).Append('=').Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
