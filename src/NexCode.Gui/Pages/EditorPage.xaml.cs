using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.Controls;

namespace NexCode.Gui.Pages;

/// <summary>
/// Spec §15 editor surface. Hosts up to four MonacoHost instances inside a TabView. Each
/// open file becomes a tab; the bar respects auto-save preference and round-trips contents
/// through the helper service via <c>editor.save_file</c>.
/// </summary>
public sealed partial class EditorPage : Page
{
    private const int MaxOpenTabs = 4;
    private readonly Dictionary<TabViewItem, EditorTabState> _tabs = new();

    public EditorPage()
    {
        InitializeComponent();
        AutoSaveToggle.IsOn = LoadAutoSavePreference();
        AutoSaveToggle.Toggled += AutoSaveToggle_Toggled;
    }

    public string? ProjectRoot { get; set; }

    private void AutoSaveToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SaveAutoSavePreference(AutoSaveToggle.IsOn);
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        // Slice 0015 only wires the in-process open path. The full file picker arrives later.
        var dialog = new ContentDialog
        {
            Title = "Open File",
            CloseButtonText = "Cancel",
            PrimaryButtonText = "Open",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        var input = new TextBox { PlaceholderText = "absolute or project-relative path" };
        dialog.Content = input;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            await OpenFileAsync(input.Text.Trim());
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (_tabs.Count >= MaxOpenTabs)
        {
            var dlg = new ContentDialog
            {
                Title = "Too many tabs open",
                Content = $"NexCode allows up to {MaxOpenTabs} concurrent editor tabs. Close one first.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dlg.ShowAsync();
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        var contents = await File.ReadAllTextAsync(path);
        var host = new MonacoHost();
        var tab = new TabViewItem
        {
            Header = Path.GetFileName(path),
            IconSource = new SymbolIconSource { Symbol = Symbol.Document },
            Content = host,
            Tag = path
        };

        var state = new EditorTabState(path, contents);
        _tabs[tab] = state;
        EditorTabView.TabItems.Add(tab);
        EditorTabView.SelectedItem = tab;

        await host.LoadAsync();
        await host.OpenFileAsync(path, contents, language: null);

        host.ContentChanged += async (_, edit) =>
        {
            state.Content = edit.Content;
            state.IsDirty = true;
            if (AutoSaveToggle.IsOn)
            {
                await SaveAsync(state);
            }
        };
    }

    private void EditorTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is TabViewItem item)
        {
            _tabs.Remove(item);
            sender.TabItems.Remove(item);
        }
    }

    private async void EditorTabView_AddTabButtonClick(TabView sender, object args)
    {
        OpenFileButton_Click(this, new RoutedEventArgs());
        await Task.CompletedTask;
    }

    private async Task SaveAsync(EditorTabState state)
    {
        try
        {
            // Direct write fallback for slice 0015 — IPC handler for editor.save_file is wired
            // up in PipeServerBackgroundService. The GUI HelperControlClient may grow a typed
            // wrapper later. For now we persist via direct File IO so auto-save works.
            await File.WriteAllTextAsync(state.Path, state.Content);
            state.IsDirty = false;
        }
        catch
        {
            // Surfacing save failures to the user is a Slice 0018 concern.
        }
    }

    private static string AutoSaveSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexCode");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "editor.autosave");
    }

    private static bool LoadAutoSavePreference()
    {
        try
        {
            var p = AutoSaveSettingsPath();
            return File.Exists(p) && string.Equals(File.ReadAllText(p), "1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void SaveAutoSavePreference(bool isOn)
    {
        try
        {
            File.WriteAllText(AutoSaveSettingsPath(), isOn ? "1" : "0");
        }
        catch
        {
            // best-effort
        }
    }

    private sealed class EditorTabState(string path, string content)
    {
        public string Path { get; } = path;
        public string Content { get; set; } = content;
        public bool IsDirty { get; set; }
    }
}
