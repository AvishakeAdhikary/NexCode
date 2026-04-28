using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>write_file</c>: writes UTF-8 text to a file under the active project
/// root, creating parent directories on demand. PermissionRequirement is
/// <see cref="ToolPermissionRequirement.Warned"/> so Default Access flows through the
/// permission gate before any disk mutation occurs.
/// </summary>
public sealed class WriteFileTool : ITool
{
    public string Name => "write_file";

    public string Description =>
        "Write UTF-8 text content to a file inside the active project root. Creates parent directories by default.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Warned;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Project-relative or absolute path of the file to write." },
            content = new { type = "string", description = "UTF-8 text payload to persist." },
            create_directories = new { type = "boolean", description = "Create missing parent directories.", @default = true }
        },
        required = new[] { "path", "content" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<WriteFileArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for write_file.");

        if (string.IsNullOrWhiteSpace(arguments.Path))
        {
            return Error(context, "missing_path", "The 'path' argument is required.");
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.Path, out var fullPath))
        {
            return Error(context, "path_outside_root", $"Path '{arguments.Path}' resolves outside the active project root.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var createDirectories = arguments.CreateDirectories ?? true;
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            if (!createDirectories)
            {
                return Error(context, "directory_missing", $"Parent directory '{directory}' does not exist and create_directories is false.");
            }
            Directory.CreateDirectory(directory);
        }

        var bytes = Encoding.UTF8.GetBytes(arguments.Content ?? string.Empty);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

        var payload = JsonSerializer.Serialize(
            new WriteFileResult(fullPath, bytes.Length),
            JsonSerialization.Options);

        return new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false,
            FilesChanged: new[] { fullPath });
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(
            new { error = code, message },
            JsonSerialization.Options);
        return new ToolOutcome(
            ToolName: "write_file",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record WriteFileArguments(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("create_directories")] bool? CreateDirectories);

    private sealed record WriteFileResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("bytes_written")] int BytesWritten);
}
