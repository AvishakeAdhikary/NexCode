using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>list_directory</c>: enumerates the top-level entries of a directory
/// inside the active project root, returning at most <c>max_entries</c> items sorted
/// directories-first then by case-insensitive name.
/// </summary>
public sealed class ListDirectoryTool : ITool
{
    private const int DefaultMaxEntries = 50;

    public string Name => "list_directory";

    public string Description =>
        "List the contents of a directory inside the active project root, sorted directories-first.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Project-relative or absolute directory path." },
            max_entries = new { type = "integer", minimum = 1, description = "Maximum number of entries to include.", @default = DefaultMaxEntries }
        },
        required = new[] { "path" }
    });

    public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<ListDirectoryArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for list_directory.");

        if (string.IsNullOrWhiteSpace(arguments.Path))
        {
            return Task.FromResult(Error(context, "missing_path", "The 'path' argument is required."));
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.Path, out var fullPath))
        {
            return Task.FromResult(Error(context, "path_outside_root", $"Path '{arguments.Path}' resolves outside the active project root."));
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(Error(context, "directory_not_found", $"Directory '{fullPath}' does not exist."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var maxEntries = arguments.MaxEntries is > 0 ? arguments.MaxEntries.Value : DefaultMaxEntries;
        var allEntries = Directory.EnumerateFileSystemEntries(fullPath)
            .Select(entryPath => new DirectoryEntryDto(
                Name: Path.GetFileName(entryPath),
                Type: Directory.Exists(entryPath) ? "directory" : "file"))
            .OrderBy(item => item.Type, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visible = allEntries.Take(maxEntries).ToArray();
        var payload = JsonSerializer.Serialize(
            new ListDirectoryResult(
                Path: fullPath,
                EntryCount: allEntries.Length,
                Entries: visible,
                Truncated: allEntries.Length > visible.Length),
            JsonSerialization.Options);

        return Task.FromResult(new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false));
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(
            new { error = code, message },
            JsonSerialization.Options);
        return new ToolOutcome(
            ToolName: "list_directory",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record ListDirectoryArguments(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("max_entries")] int? MaxEntries);

    private sealed record ListDirectoryResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("entry_count")] int EntryCount,
        [property: JsonPropertyName("entries")] DirectoryEntryDto[] Entries,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record DirectoryEntryDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type);
}
