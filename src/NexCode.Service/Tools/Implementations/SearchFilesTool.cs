using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>search_files</c>: recursively scans the project root for lines matching
/// either a literal substring or a regular expression. Skips noisy build directories,
/// caps the match count, and yields an 80-character preview centered on the hit so the
/// model has enough context without flooding the turn.
/// </summary>
public sealed class SearchFilesTool : ITool
{
    private const int DefaultMaxMatches = 200;
    private const int PreviewWidth = 80;

    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules"
    };

    public string Name => "search_files";

    public string Description =>
        "Recursively search project files for a literal substring or regex pattern. Returns line/column previews for each match.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "Substring or regex to find." },
            is_regex = new { type = "boolean", description = "Treat pattern as a .NET regular expression.", @default = false },
            max_matches = new { type = "integer", minimum = 1, description = "Cap on returned match records.", @default = DefaultMaxMatches },
            include_glob = new { type = "string", description = "Glob filter applied to each candidate path.", @default = "**/*" },
            ignore_case = new { type = "boolean", description = "Case-insensitive matching.", @default = true }
        },
        required = new[] { "pattern" }
    });

    public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.Deserialize<SearchFilesArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for search_files.");

        if (string.IsNullOrEmpty(arguments.Pattern))
        {
            return Task.FromResult(Error(context, "missing_pattern", "The 'pattern' argument is required."));
        }

        var ignoreCase = arguments.IgnoreCase ?? true;
        var maxMatches = arguments.MaxMatches is > 0 ? arguments.MaxMatches.Value : DefaultMaxMatches;
        var includeGlob = string.IsNullOrWhiteSpace(arguments.IncludeGlob) ? "**/*" : arguments.IncludeGlob!;
        var isRegex = arguments.IsRegex ?? false;

        Regex regex;
        try
        {
            regex = isRegex
                ? new Regex(arguments.Pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(2))
                : new Regex(Regex.Escape(arguments.Pattern), ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(Error(context, "invalid_pattern", ex.Message));
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(includeGlob);

        var rootFull = Path.GetFullPath(context.ProjectRoot);
        var matches = new List<MatchDto>(capacity: Math.Min(maxMatches, 64));
        var truncated = false;

        foreach (var filePath in EnumerateCandidateFiles(rootFull, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(rootFull, filePath).Replace(Path.DirectorySeparatorChar, '/');
            if (!matcher.Match(relative).HasMatches)
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath, Encoding.UTF8);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                Match match;
                try
                {
                    match = regex.Match(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    break;
                }

                while (match.Success)
                {
                    if (matches.Count >= maxMatches)
                    {
                        truncated = true;
                        break;
                    }

                    matches.Add(new MatchDto(
                        Path: filePath,
                        Line: lineIndex + 1,
                        Column: match.Index + 1,
                        Preview: BuildPreview(line, match.Index, match.Length)));

                    if (match.Length == 0)
                    {
                        break;
                    }

                    try
                    {
                        match = match.NextMatch();
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        break;
                    }
                }

                if (truncated)
                {
                    break;
                }
            }

            if (truncated)
            {
                break;
            }
        }

        var payload = JsonSerializer.Serialize(
            new SearchFilesResult(arguments.Pattern, matches.ToArray(), truncated),
            JsonSerialization.Options);

        return Task.FromResult(new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false));
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();

            string[] subDirectories;
            try
            {
                subDirectories = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                var dirName = Path.GetFileName(subDirectory);
                if (SkipDirectoryNames.Contains(dirName))
                {
                    continue;
                }
                stack.Push(subDirectory);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static string BuildPreview(string line, int matchIndex, int matchLength)
    {
        if (line.Length <= PreviewWidth)
        {
            return line;
        }

        var matchCenter = matchIndex + matchLength / 2;
        var start = Math.Max(0, matchCenter - PreviewWidth / 2);
        if (start + PreviewWidth > line.Length)
        {
            start = Math.Max(0, line.Length - PreviewWidth);
        }
        return line.Substring(start, Math.Min(PreviewWidth, line.Length - start));
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(
            new { error = code, message },
            JsonSerialization.Options);
        return new ToolOutcome(
            ToolName: "search_files",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record SearchFilesArguments(
        [property: JsonPropertyName("pattern")] string Pattern,
        [property: JsonPropertyName("is_regex")] bool? IsRegex,
        [property: JsonPropertyName("max_matches")] int? MaxMatches,
        [property: JsonPropertyName("include_glob")] string? IncludeGlob,
        [property: JsonPropertyName("ignore_case")] bool? IgnoreCase);

    private sealed record SearchFilesResult(
        [property: JsonPropertyName("pattern")] string Pattern,
        [property: JsonPropertyName("matches")] MatchDto[] Matches,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record MatchDto(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("line")] int Line,
        [property: JsonPropertyName("column")] int Column,
        [property: JsonPropertyName("preview")] string Preview);
}
