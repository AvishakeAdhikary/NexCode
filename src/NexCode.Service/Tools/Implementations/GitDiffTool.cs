using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Git;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>git_diff</c> tool. Returns a unified diff between two refs (or HEAD vs the
/// working tree when neither is supplied). Read-only, so the permission requirement is
/// <see cref="ToolPermissionRequirement.Default"/>.
/// </summary>
public sealed class GitDiffTool(ICheckpointService checkpointService) : ITool
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(
        new
        {
            type = "object",
            properties = new
            {
                from_ref = new
                {
                    type = "string",
                    description = "Source ref/commit. Defaults to HEAD when omitted."
                },
                to_ref = new
                {
                    type = "string",
                    description = "Target ref/commit. Defaults to working tree when omitted."
                }
            },
            additionalProperties = false
        },
        JsonSerialization.Options);

    public string Name => "git_diff";

    public string Description =>
        "Return a unified diff between two refs in the active project (defaults to HEAD vs working tree).";

    public JsonElement InputSchema => Schema;

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isRepository = LibGit2Sharp.Repository.IsValid(context.ProjectRoot);
        if (!isRepository)
        {
            var emptyPayload = JsonSerializer.Serialize(
                new GitDiffToolResult(string.Empty, false),
                JsonSerialization.Options);
            return new ToolOutcome(Name, context.CallId, emptyPayload, IsError: false);
        }

        GitDiffToolArguments? args = null;
        if (!string.IsNullOrWhiteSpace(context.ArgumentsJson))
        {
            try
            {
                args = JsonSerializer.Deserialize<GitDiffToolArguments>(
                    context.ArgumentsJson,
                    JsonSerialization.Options);
            }
            catch (JsonException)
            {
                args = null;
            }
        }

        var fromRef = args?.FromRef ?? string.Empty;
        var toRef = args?.ToRef ?? string.Empty;

        var unifiedDiff = await checkpointService
            .GetDiffAsync(context.ProjectRoot, fromRef, toRef, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new GitDiffToolResult(unifiedDiff, true),
            JsonSerialization.Options);

        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    internal sealed record GitDiffToolArguments(
        [property: JsonPropertyName("from_ref")] string? FromRef,
        [property: JsonPropertyName("to_ref")] string? ToRef);

    private sealed record GitDiffToolResult(
        [property: JsonPropertyName("unified_diff")] string UnifiedDiff,
        [property: JsonPropertyName("is_repository")] bool IsRepository);
}
