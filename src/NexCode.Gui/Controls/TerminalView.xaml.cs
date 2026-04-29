using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Shared.Json;

namespace NexCode.Gui.Controls;

/// <summary>
/// Spec §6 / §15 terminal surface. Hosts xterm.js inside a WebView2 and bridges its
/// keystrokes to the helper service via <c>terminal.write</c>. Output bytes pushed by
/// the helper through the <c>terminal.output</c> service event are fed into xterm.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    public event EventHandler<TerminalSpawnRequested>? SpawnRequested;
    public event EventHandler<TerminalInputRequested>? InputRequested;
    public event EventHandler<TerminalKillRequested>? KillRequested;
    public event EventHandler<TerminalResizeRequested>? ResizeRequested;

    private bool _initialized;
    private string? _terminalId;

    public TerminalView()
    {
        InitializeComponent();
    }

    public string? TerminalId => _terminalId;

    public string SelectedShell => (ShellComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "pwsh";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

        var indexPath = ResolveTerminalIndex();
        WebView.CoreWebView2.Navigate(new Uri(indexPath, UriKind.Absolute).AbsoluteUri);
        _initialized = true;
    }

    public void AttachTerminal(string terminalId)
    {
        _terminalId = terminalId;
        StatusTextBlock.Text = $"running ({terminalId})";
        StartButton.IsEnabled = false;
        KillButton.IsEnabled = true;
    }

    public void DetachTerminal(int? exitCode = null)
    {
        StatusTextBlock.Text = exitCode is null ? "stopped" : $"exited ({exitCode})";
        StartButton.IsEnabled = true;
        KillButton.IsEnabled = false;
        _terminalId = null;
    }

    public void PushOutput(string base64)
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var msg = JsonSerializer.Serialize(new { type = "output", data = text }, JsonSerialization.Options);
            WebView.CoreWebView2.PostWebMessageAsString(msg);
        }
        catch
        {
            // ignore malformed payloads
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        await InitializeAsync();
        SpawnRequested?.Invoke(this, new TerminalSpawnRequested(SelectedShell));
    }

    private void KillButton_Click(object sender, RoutedEventArgs e)
    {
        if (_terminalId is null)
        {
            return;
        }
        KillRequested?.Invoke(this, new TerminalKillRequested(_terminalId));
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); }
        catch { raw = e.WebMessageAsJson; }
        if (string.IsNullOrWhiteSpace(raw)) return;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            switch (typeProp.GetString())
            {
                case "input":
                    if (_terminalId is not null && root.TryGetProperty("data", out var dataProp))
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(dataProp.GetString() ?? string.Empty);
                        InputRequested?.Invoke(this, new TerminalInputRequested(_terminalId, Convert.ToBase64String(bytes)));
                    }
                    break;
                case "resize":
                    if (_terminalId is not null
                        && root.TryGetProperty("cols", out var colsProp)
                        && root.TryGetProperty("rows", out var rowsProp))
                    {
                        ResizeRequested?.Invoke(this, new TerminalResizeRequested(_terminalId, colsProp.GetInt32(), rowsProp.GetInt32()));
                    }
                    break;
                case "ready":
                    StatusTextBlock.Text = "ready";
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string ResolveTerminalIndex()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Assets", "Terminal", "index.html");
        return File.Exists(candidate) ? candidate : Path.Combine(baseDir, "Terminal", "index.html");
    }
}

public sealed record TerminalSpawnRequested(string Shell);
public sealed record TerminalInputRequested(string TerminalId, string DataBase64);
public sealed record TerminalKillRequested(string TerminalId);
public sealed record TerminalResizeRequested(string TerminalId, int Cols, int Rows);
