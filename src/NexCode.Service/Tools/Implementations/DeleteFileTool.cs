using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>delete_file</c>: removes a single file inside the active project root.
/// Refuses directories and missing targets so the model never silently no-ops a delete.
/// PermissionRequirement is <see cref="ToolPermissionRequirement.Full"/> — destructive.
/// </summary>
public sealed class DeleteFileTool : ITool
{
    public string Name => "delete_file";

    public string Description =>
        "Delete a single file inside the active project root. Errors if the target is missing or is a directory.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Full;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Project-relative or absolute path of the file to delete." }
        },
        required = new[] { "path" }
    });

    public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<DeleteFileArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for delete_file.");

        if (string.IsNullOrWhiteSpace(arguments.Path))
        {
            return Task.FromResult(Error(context, "missing_path", "The 'path' argument is required."));
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.Path, out var fullPath))
        {
            return Task.FromResult(Error(context, "path_outside_root", $"Path '{arguments.Path}' resolves outside the active project root."));
        }

        if (Directory.Exists(fullPath))
        {
            return Task.FromResult(Error(context, "is_directory", $"Path '{fullPath}' refers to a directory; delete_file only removes files."));
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(Error(context, "file_not_found", $"File '{fullPath}' does not exist."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(fullPath);

        var payload = JsonSerializer.Serialize(
            new DeleteFileResult(fullPath, true),
            JsonSerialization.Options);

        return Task.FromResult(new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false,
            FilesChanged: new[] { fullPath }));
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(
            new { error = code, message },
            JsonSerialization.Options);
        return new ToolOutcome(
            ToolName: "delete_file",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record DeleteFileArguments(
        [property: JsonPropertyName("path")] string Path);

    private sealed record DeleteFileResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("deleted")] bool Deleted);
}
