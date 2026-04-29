using System;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Reusable JSON editor surface for sheet-style edits across MCP, Modes, Plugin manifest, etc.
/// Slice 0015 will swap the inner <see cref="TextBox"/> for a Monaco-backed editor; until then
/// this control supports format / save / reload / revert with a per-instance <c>Saved</c> hook.
/// </summary>
public sealed partial class JsonEditorSheet : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(JsonEditorSheet), new PropertyMetadata("JSON editor"));

    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(string), typeof(JsonEditorSheet), new PropertyMetadata("{}"));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(JsonEditorSheet), new PropertyMetadata(string.Empty));

    private string _baselineDocument = "{}";

    public JsonEditorSheet()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Document
    {
        get => (string)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public event EventHandler<string>? Saved;
    public event EventHandler? Closed;
    public event EventHandler? ReloadRequested;

    public void SetBaseline(string document)
    {
        _baselineDocument = document ?? "{}";
        Document = _baselineDocument;
        Status = string.Empty;
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(Document);
            Document = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            Status = "Formatted.";
        }
        catch (Exception ex)
        {
            Status = $"Format failed: {ex.Message}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = JsonDocument.Parse(Document);
            Saved?.Invoke(this, Document);
            _baselineDocument = Document;
            Status = "Saved.";
        }
        catch (Exception ex)
        {
            Status = $"Invalid JSON: {ex.Message}";
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadRequested?.Invoke(this, EventArgs.Empty);
        Status = "Reload requested.";
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        Document = _baselineDocument;
        Status = "Reverted to last saved baseline.";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Closed?.Invoke(this, EventArgs.Empty);
}
