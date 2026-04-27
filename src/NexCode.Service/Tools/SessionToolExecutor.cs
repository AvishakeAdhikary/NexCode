using System.Text.Json;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools;

public sealed class SessionToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(
        SessionRuntimeState session,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        return toolName switch
        {
            "list_directory" => ExecuteListDirectoryAsync(session, argumentsJson, cancellationToken),
            _ => Task.FromResult(new ToolExecutionResult(
                ToolName: toolName,
                ResultJson: JsonSerializer.Serialize(
                    new { error = $"Unknown tool '{toolName}'." },
                    JsonSerialization.Options),
                IsError: true))
        };
    }

    private static Task<ToolExecutionResult> ExecuteListDirectoryAsync(
        SessionRuntimeState session,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<ListDirectoryToolArguments>(
                            argumentsJson,
                            JsonSerialization.Options)
                        ?? new ListDirectoryToolArguments(session.Request.ProjectPath, 6);
        var normalizedPath = Path.GetFullPath(arguments.Path);
        var projectRoot = Path.GetFullPath(session.Request.ProjectPath);

        if (!normalizedPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var deniedResult = new ListDirectoryToolResult(
                Path: normalizedPath,
                EntryCount: 0,
                Entries: [],
                Truncated: false,
                Warning: "Requested path is outside the active project root.");

            return Task.FromResult(new ToolExecutionResult(
                ToolName: "list_directory",
                ResultJson: JsonSerializer.Serialize(deniedResult, JsonSerialization.Options),
                IsError: true));
        }

        if (!Directory.Exists(normalizedPath))
        {
            var missingResult = new ListDirectoryToolResult(
                Path: normalizedPath,
                EntryCount: 0,
                Entries: [],
                Truncated: false,
                Warning: "Requested directory does not exist.");

            return Task.FromResult(new ToolExecutionResult(
                ToolName: "list_directory",
                ResultJson: JsonSerializer.Serialize(missingResult, JsonSerialization.Options),
                IsError: true));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var maxEntries = Math.Clamp(arguments.MaxEntries, 1, 12);
        var entries = Directory.EnumerateFileSystemEntries(normalizedPath)
            .Select(path => new DirectoryEntryResult(
                Name: Path.GetFileName(path),
                EntryType: Directory.Exists(path) ? "directory" : "file"))
            .OrderBy(item => item.EntryType)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visibleEntries = entries.Take(maxEntries).ToArray();
        var result = new ListDirectoryToolResult(
            Path: normalizedPath,
            EntryCount: entries.Length,
            Entries: visibleEntries,
            Truncated: entries.Length > visibleEntries.Length,
            Warning: null);

        return Task.FromResult(new ToolExecutionResult(
            ToolName: "list_directory",
            ResultJson: JsonSerializer.Serialize(result, JsonSerialization.Options),
            IsError: false));
    }

    private sealed record ListDirectoryToolArguments(
        string Path,
        int MaxEntries);

    private sealed record ListDirectoryToolResult(
        string Path,
        int EntryCount,
        DirectoryEntryResult[] Entries,
        bool Truncated,
        string? Warning);

    private sealed record DirectoryEntryResult(
        string Name,
        string EntryType);
}

public sealed record ToolExecutionResult(
    string ToolName,
    string ResultJson,
    bool IsError);
