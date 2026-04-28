using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Git;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>git_revert</c> tool. Performs a hard reset of the active project root to a
/// previously captured checkpoint commit. Destructive, so its
/// <see cref="PermissionRequirement"/> is <see cref="ToolPermissionRequirement.Full"/>.
/// </summary>
public sealed class GitRevertTool(ICheckpointService checkpointService) : ITool
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(
        new
        {
            type = "object",
            properties = new
            {
                commit_hash = new
                {
                    type = "string",
                    description = "Checkpoint commit hash to hard-reset the working tree to."
                }
            },
            required = new[] { "commit_hash" },
            additionalProperties = false
        },
        JsonSerialization.Options);

    public string Name => "git_revert";

    public string Description =>
        "Hard-reset the active project root to a previously captured checkpoint commit.";

    public JsonElement InputSchema => Schema;

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Full;

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        GitRevertToolArguments? args = null;
        if (!string.IsNullOrWhiteSpace(context.ArgumentsJson))
        {
            try
            {
                args = JsonSerializer.Deserialize<GitRevertToolArguments>(
                    context.ArgumentsJson,
                    JsonSerialization.Options);
            }
            catch (JsonException)
            {
                args = null;
            }
        }

        if (args is null || string.IsNullOrWhiteSpace(args.CommitHash))
        {
            var errorPayload = JsonSerializer.Serialize(
                new { error = "missing_commit_hash", message = "The 'commit_hash' argument is required." },
                JsonSerialization.Options);
            return new ToolOutcome(Name, context.CallId, errorPayload, IsError: true);
        }

        var reverted = await checkpointService
            .RevertAsync(context.ProjectRoot, args.CommitHash, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new GitRevertToolResult(reverted, args.CommitHash),
            JsonSerialization.Options);

        return new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: !reverted,
            FilesChanged: reverted ? Array.Empty<string>() : null);
    }

    internal sealed record GitRevertToolArguments(
        [property: JsonPropertyName("commit_hash")] string CommitHash);

    private sealed record GitRevertToolResult(
        [property: JsonPropertyName("reverted")] bool Reverted,
        [property: JsonPropertyName("commit_hash")] string CommitHash);
}
