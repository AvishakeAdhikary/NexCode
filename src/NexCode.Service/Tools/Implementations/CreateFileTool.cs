using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>create_file</c>: creates a brand-new UTF-8 file under the active project
/// root and writes optional initial content. Errors if the target already exists so the
/// operation is unambiguous about whether disk state changed.
/// </summary>
public sealed class CreateFileTool : ITool
{
    public string Name => "create_file";

    public string Description =>
        "Create a new UTF-8 text file inside the active project root. Errors if the target already exists.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Project-relative or absolute path of the file to create." },
            content = new { type = "string", description = "Optional initial UTF-8 content. Empty string when omitted." }
        },
        required = new[] { "path" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<CreateFileArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for create_file.");

        if (string.IsNullOrWhiteSpace(arguments.Path))
        {
            return Error(context, "missing_path", "The 'path' argument is required.");
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.Path, out var fullPath))
        {
            return Error(context, "path_outside_root", $"Path '{arguments.Path}' resolves outside the active project root.");
        }

        if (File.Exists(fullPath))
        {
            return Error(context, "file_exists", $"File '{fullPath}' already exists.");
        }

        if (Directory.Exists(fullPath))
        {
            return Error(context, "is_directory", $"Path '{fullPath}' refers to an existing directory.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = Encoding.UTF8.GetBytes(arguments.Content ?? string.Empty);
        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }

        var payload = JsonSerializer.Serialize(
            new CreateFileResult(fullPath, bytes.Length),
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
            ToolName: "create_file",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record CreateFileArguments(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record CreateFileResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("bytes_written")] int BytesWritten);
}
