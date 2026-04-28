using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>read_file</c>: reads a UTF-8 file under the active project root and
/// returns its contents (truncated to a byte budget) so the model can reason over them.
/// </summary>
public sealed class ReadFileTool : ITool
{
    private const int DefaultMaxBytes = 256_000;

    public string Name => "read_file";

    public string Description =>
        "Read the contents of a UTF-8 text file located inside the active project root. Optionally truncates to a byte budget.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Project-relative or absolute path to the file." },
            max_bytes = new { type = "integer", minimum = 1, description = "Maximum number of bytes to read.", @default = DefaultMaxBytes }
        },
        required = new[] { "path" }
    });

    public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<ReadFileArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for read_file.");

        if (string.IsNullOrWhiteSpace(arguments.Path))
        {
            return Task.FromResult(Error(context, "missing_path", "The 'path' argument is required."));
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.Path, out var fullPath))
        {
            return Task.FromResult(Error(context, "path_outside_root", $"Path '{arguments.Path}' resolves outside the active project root."));
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(Error(context, "file_not_found", $"File '{fullPath}' does not exist."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var maxBytes = arguments.MaxBytes is > 0 ? arguments.MaxBytes.Value : DefaultMaxBytes;
        var fileInfo = new FileInfo(fullPath);
        var truncated = fileInfo.Length > maxBytes;

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesToRead = (int)Math.Min(fileInfo.Length, maxBytes);
        var buffer = new byte[bytesToRead];
        var totalRead = 0;
        while (totalRead < bytesToRead)
        {
            var read = stream.Read(buffer, totalRead, bytesToRead - totalRead);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }

        var content = Encoding.UTF8.GetString(buffer, 0, totalRead);
        var payload = JsonSerializer.Serialize(
            new ReadFileResult(fullPath, fileInfo.Length, truncated, content),
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
            ToolName: "read_file",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record ReadFileArguments(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("max_bytes")] int? MaxBytes);

    private sealed record ReadFileResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("truncated")] bool Truncated,
        [property: JsonPropertyName("content")] string Content);
}
