using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NexCode.Gui.Controls;

/// <summary>
/// Reusable side-sheet that hosts a <see cref="MonacoHost"/> configured with one of the
/// built-in NexCode JSON schemas (mode, MCP, plugin, keybindings). The sheet is purely
/// visual; schema-aware validation happens inside Monaco via the <c>json</c> language and
/// optional schema URL.
/// </summary>
public sealed partial class JsonSchemaEditorSheet : UserControl
{
    public event EventHandler<JsonSchemaEditorResult>? Saved;
    public event EventHandler? Closed;
    public event EventHandler<string>? ValidationFailed;

    private string? _activeSchemaId;

    public JsonSchemaEditorSheet()
    {
        InitializeComponent();
    }

    public static IReadOnlyList<JsonSchemaDescriptor> KnownSchemas { get; } = new[]
    {
        new JsonSchemaDescriptor("mode", "NexCode Mode (.nexmode)", "nexmode.schema.json"),
        new JsonSchemaDescriptor("mcp", "MCP Server config", "mcp-server.schema.json"),
        new JsonSchemaDescriptor("plugin", "NexCode plugin manifest", "nexcode-plugin.schema.json"),
        new JsonSchemaDescriptor("keybindings", "Key bindings (.nexkeys)", "nexkeys.schema.json")
    };

    public async Task OpenAsync(string schemaId, string title, string initialJson)
    {
        _activeSchemaId = schemaId;
        TitleTextBlock.Text = title;
        SchemaIdTextBlock.Text = $"schema: {schemaId}";

        await EditorHost.LoadAsync();
        await EditorHost.OpenFileAsync($"untitled-{schemaId}.json", initialJson, "json");
    }

    public Task<string> GetContentAsync() => EditorHost.GetContentAsync();

    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        var content = await EditorHost.GetContentAsync();
        if (TryValidate(content, out var error))
        {
            ValidationFailed?.Invoke(this, string.Empty);
        }
        else
        {
            ValidationFailed?.Invoke(this, error);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var content = await EditorHost.GetContentAsync();
        if (!TryValidate(content, out var error))
        {
            ValidationFailed?.Invoke(this, error);
            return;
        }

        Saved?.Invoke(this, new JsonSchemaEditorResult(_activeSchemaId ?? string.Empty, content));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryValidate(string content, out string error)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Document is empty.";
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(content);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string ResolveSchemaPath(string schemaFileName)
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Assets", "Schemas", schemaFileName);
    }
}

public sealed record JsonSchemaDescriptor(string Id, string Title, string SchemaFile);

public sealed record JsonSchemaEditorResult(string SchemaId, string Content);
