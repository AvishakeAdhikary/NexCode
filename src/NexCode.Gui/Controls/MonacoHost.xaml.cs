using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Gui.Controls;

/// <summary>
/// WinUI 3 UserControl that hosts the Monaco editor inside a WebView2.
/// The HTML payload (Assets/Monaco/index.html) and this control communicate via
/// <c>window.chrome.webview.postMessage</c> in both directions.
/// </summary>
public sealed partial class MonacoHost : UserControl
{
    private readonly TaskCompletionSource<bool> _readyTcs = new();
    private string? _pendingPath;

    public event EventHandler<MonacoEdit>? ContentChanged;

    public MonacoHost()
    {
        InitializeComponent();
    }

    /// <summary>Loads the bundled Monaco HTML page into the WebView2 and waits for ready.</summary>
    public async Task LoadAsync()
    {
        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        var indexPath = ResolveIndexPath();
        var uri = new Uri(indexPath, UriKind.Absolute);
        WebView.CoreWebView2.Navigate(uri.AbsoluteUri);

        // Wait up to 10s for the page to call back with { type: 'ready' }.
        var winner = await Task.WhenAny(_readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (winner != _readyTcs.Task)
        {
            // Continue anyway; the editor may still come up after navigation finishes.
        }
    }

    public Task OpenFileAsync(string path, string contents, string? language)
    {
        _pendingPath = path;
        return PostAsync(new
        {
            type = "open",
            path,
            content = contents,
            language = string.IsNullOrWhiteSpace(language) ? GuessLanguage(path) : language
        });
    }

    /// <summary>Round-trips with the page to capture the latest editor buffer.</summary>
    public async Task<string> GetContentAsync()
    {
        try
        {
            // Ask the page to post its current content back; we listen via WebMessageReceived.
            var tcs = new TaskCompletionSource<string>();
            EventHandler<string> handler = (_, value) => tcs.TrySetResult(value);
            _contentResponded += handler;
            try
            {
                await PostAsync(new { type = "getContent" });
                var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                return winner == tcs.Task ? tcs.Task.Result : string.Empty;
            }
            finally
            {
                _contentResponded -= handler;
            }
        }
        catch
        {
            // Best-effort fallback: return empty if WebView is not ready.
            return string.Empty;
        }
    }

    public Task SetLanguageAsync(string language)
        => PostAsync(new { type = "setLanguage", language });

    public Task SetDiagnosticsAsync(IReadOnlyList<LinterMarker> markers)
    {
        return PostAsync(new
        {
            type = "setDiagnostics",
            markers
        });
    }

    private event EventHandler<string>? _contentResponded;

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

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(raw).RootElement;
        }
        catch
        {
            return;
        }

        if (!root.TryGetProperty("type", out var typeProp))
        {
            return;
        }

        var messageType = typeProp.GetString();
        switch (messageType)
        {
            case "ready":
                _readyTcs.TrySetResult(true);
                break;
            case "contentChanged":
                if (root.TryGetProperty("value", out var valueProp))
                {
                    ContentChanged?.Invoke(this, new MonacoEdit(_pendingPath, valueProp.GetString() ?? string.Empty));
                }
                break;
            case "content":
                if (root.TryGetProperty("value", out var content))
                {
                    _contentResponded?.Invoke(this, content.GetString() ?? string.Empty);
                }
                break;
        }
    }

    internal Task PostAsync(object payload)
    {
        if (WebView.CoreWebView2 is null)
        {
            return Task.CompletedTask;
        }

        var json = JsonSerializer.Serialize(payload, JsonSerialization.Options);
        WebView.CoreWebView2.PostWebMessageAsString(json);
        return Task.CompletedTask;
    }

    internal static string ResolveIndexPath(string fileName = "index.html")
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Assets", "Monaco", fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Fallback for loose layout / tests.
        var alt = Path.Combine(baseDir, "Monaco", fileName);
        return File.Exists(alt) ? alt : candidate;
    }

    private static string GuessLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
            ".json" => "json",
            ".md" => "markdown",
            ".py" => "python",
            ".html" => "html",
            ".css" => "css",
            ".xaml" or ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".rs" => "rust",
            ".go" => "go",
            ".java" => "java",
            ".kt" => "kotlin",
            ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "cpp",
            ".sh" => "shell",
            ".ps1" => "powershell",
            _ => "plaintext"
        };
    }
}

public sealed record MonacoEdit(string? Path, string Content);
