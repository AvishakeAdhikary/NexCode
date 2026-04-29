using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using NexCode.Shared.Json;

namespace NexCode.Gui.Controls;

/// <summary>
/// WebView2-hosted Monaco diff editor wrapper. Reuses <see cref="MonacoHost"/> assets.
/// </summary>
public sealed partial class MonacoDiffHost : UserControl
{
    private readonly TaskCompletionSource<bool> _readyTcs = new();
    private bool _initialized;

    public MonacoDiffHost()
    {
        InitializeComponent();
    }

    public async Task LoadAsync()
    {
        if (_initialized)
        {
            return;
        }

        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        var indexPath = MonacoHost.ResolveIndexPath();
        var uri = new Uri(indexPath, UriKind.Absolute);
        WebView.CoreWebView2.Navigate(uri.AbsoluteUri);

        var winner = await Task.WhenAny(_readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        _ = winner;
        _initialized = true;
    }

    public Task ShowDiffAsync(string before, string after, string? language)
    {
        if (WebView.CoreWebView2 is null)
        {
            return Task.CompletedTask;
        }

        var payload = new
        {
            type = "showDiff",
            before = before ?? string.Empty,
            after = after ?? string.Empty,
            language = string.IsNullOrWhiteSpace(language) ? "plaintext" : language
        };

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);
        WebView.CoreWebView2.PostWebMessageAsString(json);
        return Task.CompletedTask;
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try
        {
            raw = e.TryGetWebMessageAsString();
        }
        catch
        {
            raw = e.WebMessageAsJson;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("type", out var typeProp)
                && typeProp.GetString() == "ready")
            {
                _readyTcs.TrySetResult(true);
            }
        }
        catch
        {
            // ignore malformed messages
        }
    }

}
