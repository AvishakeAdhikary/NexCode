using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Git;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 <c>git_status</c> tool. Returns porcelain-formatted working-tree status for the
/// active project. Read-only, so its <see cref="PermissionRequirement"/> is
/// <see cref="ToolPermissionRequirement.Default"/>.
/// </summary>
public sealed class GitStatusTool(ICheckpointService checkpointService) : ITool
{
    private static readonly JsonElement EmptySchema = JsonSerializer.SerializeToElement(
        new
        {
            type = "object",
            properties = new { },
            additionalProperties = false
        },
        JsonSerialization.Options);

    public string Name => "git_status";

    public string Description =>
        "Show porcelain-format git status for the active project root.";

    public JsonElement InputSchema => EmptySchema;

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public async Task<ToolOutcome> ExecuteAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isRepository = LibGit2Sharp.Repository.IsValid(context.ProjectRoot);
        var porcelain = isRepository
            ? await checkpointService.GetStatusAsync(context.ProjectRoot, cancellationToken)
                .ConfigureAwait(false)
            : string.Empty;

        var payload = JsonSerializer.Serialize(
            new GitStatusToolResult(porcelain, isRepository),
            JsonSerialization.Options);

        return new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: false);
    }

    private sealed record GitStatusToolResult(
        [property: JsonPropertyName("porcelain")] string Porcelain,
        [property: JsonPropertyName("is_repository")] bool IsRepository);
}
