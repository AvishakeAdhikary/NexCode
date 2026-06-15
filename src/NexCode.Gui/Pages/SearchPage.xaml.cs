using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Services;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages
{
    /// <summary>
    /// Nav-rail global search destination (spec §32). Backed by <c>history.search</c> via
    /// <see cref="HelperControlClient"/>; renders the returned <c>HistorySearchHit</c> rows.
    /// </summary>
    public sealed partial class SearchPage : Page
    {
        public ObservableCollection<SearchHitRowViewModel> Results { get; } = new();

        private HelperControlClient? _client;

        public SearchPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _client = ((App)Application.Current).Services.GetRequiredService<HelperControlClient>();
        }

        private async void QueryBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var query = args.QueryText;
            if (string.IsNullOrWhiteSpace(query))
            {
                StatusTextBlock.Text = "Enter a query to search.";
                return;
            }

            if (_client is null)
            {
                StatusTextBlock.Text = "Helper unavailable; cannot search.";
                return;
            }

            StatusTextBlock.Text = $"Searching for '{query}'...";
            try
            {
                var response = await _client.SearchHistoryAsync(query);
                Results.Clear();
                foreach (var hit in response.Results)
                {
                    Results.Add(SearchHitRowViewModel.FromHit(hit));
                }

                StatusTextBlock.Text = Results.Count == 0
                    ? $"No results for '{query}'."
                    : $"{Results.Count} result(s) for '{query}'.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Search failed: {ex.Message}";
            }
        }
    }
}

namespace NexCode.Gui.ViewModels.Pages
{
    using NexCode.Shared.Contracts;

    /// <summary>Read-only row mirror of a <see cref="HistorySearchHit"/> for the search results list.</summary>
    public sealed class SearchHitRowViewModel
    {
        public Guid SessionId { get; set; }
        public string Snippet { get; set; } = string.Empty;
        public double Score { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public string SessionLabel => $"Session {SessionId}";
        public string ScoreLabel => $"score {Score:0.00}";
        public string CreatedAtLabel => CreatedAt.LocalDateTime.ToString("g");

        public static SearchHitRowViewModel FromHit(HistorySearchHit hit) => new()
        {
            SessionId = hit.SessionId,
            Snippet = hit.Snippet,
            Score = hit.Score,
            CreatedAt = hit.CreatedAt,
        };
    }
}
