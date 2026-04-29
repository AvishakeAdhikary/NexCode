using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NexCode.Gui.Pages;

/// <summary>
/// New-session quick-launch (spec §32). The actual session creation flow lives in the GUI
/// core's <c>ShellViewModel</c>; this page only collects the pre-flight selections and
/// reports them via the <see cref="StartSession_Click"/> handler.
/// </summary>
public sealed partial class NewSessionPage : Page
{
    /// <summary>
    /// Optional injection point for the host shell to provide an HWND for picker init. The
    /// GUI core's host registers this once the main window is constructed.
    /// </summary>
    public static Func<IntPtr>? HwndProvider { get; set; }

    public NewSessionPage()
    {
        InitializeComponent();
    }

    private async void PickProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        if (HwndProvider?.Invoke() is { } hwnd && hwnd != IntPtr.Zero)
        {
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        try
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                ProjectPathTextBox.Text = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Picker unavailable: {ex.Message}";
        }
    }

    private void StartSession_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = string.IsNullOrWhiteSpace(ProjectPathTextBox.Text)
            ? "Pick or type a project path first."
            : $"Session create requested for {ProjectPathTextBox.Text} (wire-up pending).";
    }
}
