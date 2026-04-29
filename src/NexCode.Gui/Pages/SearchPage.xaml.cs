using Microsoft.UI.Xaml.Controls;

namespace NexCode.Gui.Pages;

/// <summary>
/// Nav-rail global search destination (spec §32). The IPC fan-out across <c>history.search</c>,
/// <c>memory.list</c>, plans, and project files is wired by a future helper-side aggregator.
/// </summary>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
    }

    private void QueryBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        StatusTextBlock.Text = string.IsNullOrWhiteSpace(args.QueryText)
            ? "Enter a query to search."
            : $"Searching for '{args.QueryText}' (wire-up pending).";
    }
}
