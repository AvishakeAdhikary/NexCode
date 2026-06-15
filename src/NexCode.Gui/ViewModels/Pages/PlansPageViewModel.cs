using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Gui.Services;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.PlansPage"/>. Renders a list of plans on the left and a detail
/// pane on the right with Markdown-rendered plan content. Plans are per-session
/// (<c>plan.list</c> takes a session id), so the page shows an empty-state prompt until a
/// session is supplied via <see cref="LoadAsync"/>.
/// </summary>
public sealed partial class PlansPageViewModel : ObservableObject
{
    public ObservableCollection<PlanRowViewModel> Plans { get; } = new();

    [ObservableProperty] private PlanRowViewModel? _selectedPlan;
    [ObservableProperty] private string _detailMarkdown = "Select a plan to render its Markdown.";
    [ObservableProperty] private string _statusMessage = "Select a session to view its plans.";
    [ObservableProperty] private bool _isBusy;

    private HelperControlClient? _client;
    private Guid? _sessionId;

    /// <summary>
    /// Called by the page on activation: binds the helper transport. Plans are per-session, so
    /// the list only loads when a session id is supplied via <paramref name="sessionId"/>.
    /// </summary>
    public async Task InitializeAsync(HelperControlClient client, Guid? sessionId = null)
    {
        _client = client;
        if (sessionId is { } id)
        {
            await LoadAsync(id);
        }
        else
        {
            StatusMessage = "Select a session to view its plans.";
        }
    }

    /// <summary>Loads the plan list for a specific session.</summary>
    public async Task LoadAsync(Guid sessionId)
    {
        _sessionId = sessionId;
        if (_client is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var response = await _client.ListPlansAsync(sessionId);
            ApplyListResponse(response);
            StatusMessage = Plans.Count == 0
                ? "No plans for this session yet."
                : $"Loaded {Plans.Count} plan(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load plans: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    async partial void OnSelectedPlanChanged(PlanRowViewModel? value)
    {
        if (value is null)
        {
            DetailMarkdown = "Select a plan to render its Markdown.";
            return;
        }

        DetailMarkdown = string.IsNullOrEmpty(value.ContentMarkdown)
            ? "Loading plan..."
            : value.ContentMarkdown;

        if (_client is null || !string.IsNullOrEmpty(value.ContentMarkdown))
        {
            return;
        }

        try
        {
            var detail = await _client.GetPlanAsync(value.PlanId);
            value.HydrateFromDetail(detail);
            DetailMarkdown = value.ContentMarkdown;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load plan: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Confirm()
    {
        if (SelectedPlan is null)
        {
            StatusMessage = "Select a plan first.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot confirm.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.ConfirmPlanAsync(SelectedPlan.PlanId);
            StatusMessage = $"Confirmed {SelectedPlan.Title}.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Confirm failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RequestChanges()
    {
        if (SelectedPlan is null)
        {
            StatusMessage = "Select a plan first.";
            return;
        }

        if (_client is null)
        {
            StatusMessage = "Helper unavailable; cannot request changes.";
            return;
        }

        IsBusy = true;
        try
        {
            await _client.RequestPlanChangesAsync(SelectedPlan.PlanId, DetailMarkdown);
            StatusMessage = $"Requested changes on {SelectedPlan.Title}.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Request changes failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ReloadAsync() => _sessionId is { } id ? LoadAsync(id) : Task.CompletedTask;

    public void ApplyListResponse(PlanListResponse response)
    {
        Plans.Clear();
        foreach (var p in response.Plans)
        {
            Plans.Add(PlanRowViewModel.FromSummary(p));
        }
    }
}

public sealed partial class PlanRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _planId;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private PlanStatus _status;
    [ObservableProperty] private int _version;
    [ObservableProperty] private DateTimeOffset _updatedAt;
    [ObservableProperty] private string _contentMarkdown = string.Empty;

    public static PlanRowViewModel FromSummary(PlanSummary s) => new()
    {
        PlanId = s.PlanId,
        Title = s.Title,
        Status = s.Status,
        Version = s.Version,
        UpdatedAt = s.UpdatedAt,
        ContentMarkdown = string.Empty,
    };

    public void HydrateFromDetail(PlanDetail d)
    {
        Title = d.Title;
        Status = d.Status;
        Version = d.Version;
        UpdatedAt = d.UpdatedAt;
        ContentMarkdown = d.Content;
    }
}
