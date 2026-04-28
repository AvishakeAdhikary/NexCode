using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.6 + Appendix B <c>cut_paste_file</c>: atomically moves a contiguous range of
/// lines from one file to a target line in another file (or the same file). Each side
/// is written via a temp file and <see cref="File.Replace(string, string, string)"/> so
/// a crash mid-operation either preserves the original or surfaces a backup.
/// </summary>
public sealed class CutPasteFileTool : ITool
{
    public string Name => "cut_paste_file";

    public string Description =>
        "Atomically move a contiguous range of lines from one file to another (or within the same file). Returns a unified diff describing the change.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Warned;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            source_file = new { type = "string" },
            source_start_line = new { type = "integer", minimum = 1 },
            source_end_line = new { type = "integer", minimum = 1 },
            destination_file = new { type = "string" },
            destination_insert_line = new { type = "integer", minimum = 0 },
            leave_blank_lines = new { type = "boolean", @default = false },
            comment_marker = new { type = "string", description = "Optional comment text inserted at the source location after the cut." }
        },
        required = new[] { "source_file", "source_start_line", "source_end_line", "destination_file", "destination_insert_line" }
    });

    public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<CutPasteArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for cut_paste_file.");

        if (string.IsNullOrWhiteSpace(arguments.SourceFile) || string.IsNullOrWhiteSpace(arguments.DestinationFile))
        {
            return Task.FromResult(Error(context, "missing_path", "Both 'source_file' and 'destination_file' are required."));
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.SourceFile, out var sourceFull))
        {
            return Task.FromResult(Error(context, "source_outside_root", $"Path '{arguments.SourceFile}' resolves outside the active project root."));
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.DestinationFile, out var destFull))
        {
            return Task.FromResult(Error(context, "destination_outside_root", $"Path '{arguments.DestinationFile}' resolves outside the active project root."));
        }

        if (arguments.SourceStartLine > arguments.SourceEndLine)
        {
            return Task.FromResult(Error(context, "invalid_range", "source_start_line must be less than or equal to source_end_line."));
        }

        if (!File.Exists(sourceFull))
        {
            return Task.FromResult(Error(context, "source_not_found", $"Source file '{sourceFull}' does not exist."));
        }

        var sameFile = string.Equals(sourceFull, destFull, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && !File.Exists(destFull))
        {
            return Task.FromResult(Error(context, "destination_not_found", $"Destination file '{destFull}' does not exist."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sourceLinesBefore = File.ReadAllLines(sourceFull, Encoding.UTF8);
        if (arguments.SourceStartLine < 1 || arguments.SourceEndLine > sourceLinesBefore.Length)
        {
            return Task.FromResult(Error(context, "invalid_range", $"Source range [{arguments.SourceStartLine}, {arguments.SourceEndLine}] is outside file bounds [1, {sourceLinesBefore.Length}]."));
        }

        var destLinesBefore = sameFile ? sourceLinesBefore : File.ReadAllLines(destFull, Encoding.UTF8);
        if (arguments.DestinationInsertLine < 0 || arguments.DestinationInsertLine > destLinesBefore.Length + 1)
        {
            return Task.FromResult(Error(context, "invalid_destination_line", $"destination_insert_line must be in [0, {destLinesBefore.Length + 1}]."));
        }

        var movedLines = sourceLinesBefore
            .Skip(arguments.SourceStartLine - 1)
            .Take(arguments.SourceEndLine - arguments.SourceStartLine + 1)
            .ToArray();
        var leaveBlank = arguments.LeaveBlankLines ?? false;
        var commentMarker = arguments.CommentMarker;

        // Build new source contents.
        var sourceAfter = new List<string>(sourceLinesBefore.Length);
        sourceAfter.AddRange(sourceLinesBefore.Take(arguments.SourceStartLine - 1));
        if (!string.IsNullOrEmpty(commentMarker))
        {
            sourceAfter.Add(commentMarker);
        }
        if (leaveBlank)
        {
            for (var i = 0; i < movedLines.Length; i++)
            {
                sourceAfter.Add(string.Empty);
            }
        }
        sourceAfter.AddRange(sourceLinesBefore.Skip(arguments.SourceEndLine));

        string[] destinationAfter;
        string[] destinationBaselineForDiff;

        if (sameFile)
        {
            // Re-derive insertion index against the post-cut buffer.
            var insertIndex = arguments.DestinationInsertLine - 1;
            insertIndex = Math.Clamp(insertIndex, 0, sourceAfter.Count);

            var combined = new List<string>(sourceAfter.Count + movedLines.Length);
            combined.AddRange(sourceAfter.Take(insertIndex));
            combined.AddRange(movedLines);
            combined.AddRange(sourceAfter.Skip(insertIndex));
            destinationAfter = combined.ToArray();
            destinationBaselineForDiff = sourceLinesBefore;
        }
        else
        {
            var insertIndex = arguments.DestinationInsertLine - 1;
            insertIndex = Math.Clamp(insertIndex, 0, destLinesBefore.Length);
            var combined = new List<string>(destLinesBefore.Length + movedLines.Length);
            combined.AddRange(destLinesBefore.Take(insertIndex));
            combined.AddRange(movedLines);
            combined.AddRange(destLinesBefore.Skip(insertIndex));
            destinationAfter = combined.ToArray();
            destinationBaselineForDiff = destLinesBefore;
        }

        try
        {
            if (sameFile)
            {
                AtomicReplace(sourceFull, string.Join('\n', destinationAfter));
            }
            else
            {
                AtomicReplace(sourceFull, string.Join('\n', sourceAfter));
                AtomicReplace(destFull, string.Join('\n', destinationAfter));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(context, "atomic_replace_failed", ex.Message));
        }

        var diff = sameFile
            ? BuildUnifiedDiff(sourceFull, sourceLinesBefore, destinationAfter)
            : BuildUnifiedDiff(sourceFull, sourceLinesBefore, sourceAfter.ToArray())
              + "\n"
              + BuildUnifiedDiff(destFull, destinationBaselineForDiff, destinationAfter);

        var payload = JsonSerializer.Serialize(
            new CutPasteResult(sourceFull, destFull, movedLines.Length, diff),
            JsonSerialization.Options);

        var filesChanged = sameFile
            ? new[] { sourceFull }
            : new[] { sourceFull, destFull };

        return Task.FromResult(new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false,
            FilesChanged: filesChanged));
    }

    private static void AtomicReplace(string targetPath, string newContent)
    {
        var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var tmpPath = Path.Combine(directory, Path.GetFileName(targetPath) + ".nexcode_tmp");
        var backupPath = Path.Combine(directory, Path.GetFileName(targetPath) + ".nexcode_bak");

        File.WriteAllText(tmpPath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            File.Replace(tmpPath, targetPath, backupPath, ignoreMetadataErrors: true);
        }
        catch (FileNotFoundException)
        {
            // Target did not exist yet — fall back to a plain move so we still leave no temp behind.
            File.Move(tmpPath, targetPath, overwrite: true);
        }

        if (File.Exists(backupPath))
        {
            try { File.Delete(backupPath); }
            catch { /* best-effort cleanup */ }
        }
        if (File.Exists(tmpPath))
        {
            try { File.Delete(tmpPath); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static string BuildUnifiedDiff(string filePath, string[] before, string[] after)
    {
        var sb = new StringBuilder();
        sb.Append("--- ").Append(filePath).Append('\n');
        sb.Append("+++ ").Append(filePath).Append('\n');
        foreach (var line in before)
        {
            sb.Append('-').Append(line).Append('\n');
        }
        foreach (var line in after)
        {
            sb.Append('+').Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(
            new { error = code, message },
            JsonSerialization.Options);
        return new ToolOutcome(
            ToolName: "cut_paste_file",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record CutPasteArguments(
        [property: JsonPropertyName("source_file")] string SourceFile,
        [property: JsonPropertyName("source_start_line")] int SourceStartLine,
        [property: JsonPropertyName("source_end_line")] int SourceEndLine,
        [property: JsonPropertyName("destination_file")] string DestinationFile,
        [property: JsonPropertyName("destination_insert_line")] int DestinationInsertLine,
        [property: JsonPropertyName("leave_blank_lines")] bool? LeaveBlankLines,
        [property: JsonPropertyName("comment_marker")] string? CommentMarker);

    private sealed record CutPasteResult(
        [property: JsonPropertyName("source_file")] string SourceFile,
        [property: JsonPropertyName("destination_file")] string DestinationFile,
        [property: JsonPropertyName("lines_moved")] int LinesMoved,
        [property: JsonPropertyName("unified_diff")] string UnifiedDiff);
}
