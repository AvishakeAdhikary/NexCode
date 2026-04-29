using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Gui.ViewModels.Pages;

/// <summary>
/// VM for <see cref="Gui.Pages.PlansPage"/>. Renders a list of plans on the left and a detail
/// pane on the right with Markdown-rendered plan content plus the linked todo lists. Edit
/// surface is a placeholder until Slice 0015 brings in the Monaco-style editor.
/// </summary>
public sealed partial class PlansPageViewModel : ObservableObject
{
    public ObservableCollection<PlanRowViewModel> Plans { get; } = new();

    [ObservableProperty] private PlanRowViewModel? _selectedPlan;
    [ObservableProperty] private string _detailMarkdown = "Select a plan to render its Markdown.";
    [ObservableProperty] private string _statusMessage = "Plans loaded lazily.";

    partial void OnSelectedPlanChanged(PlanRowViewModel? value)
    {
        DetailMarkdown = value?.ContentMarkdown ?? "Select a plan to render its Markdown.";
    }

    [RelayCommand]
    private void RequestChanges() =>
        StatusMessage = SelectedPlan is null
            ? "Select a plan first."
            : $"Request changes on {SelectedPlan.Title} (wire-up pending).";

    [RelayCommand]
    private void Confirm() =>
        StatusMessage = SelectedPlan is null
            ? "Select a plan first."
            : $"Confirming {SelectedPlan.Title} (wire-up pending).";

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
