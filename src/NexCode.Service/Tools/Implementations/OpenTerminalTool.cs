using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Terminal;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 / §6 <c>open_terminal</c>: spawns an interactive ConPTY-backed terminal
/// session and returns its <c>terminal_id</c> for the GUI to attach to. Requires Full
/// permission since the spawned shell can run arbitrary commands.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OpenTerminalTool(TerminalManager terminalManager) : ITool
{
    public string Name => "open_terminal";

    public string Description =>
        "Spawn an interactive ConPTY terminal for the active project root and return the terminal_id.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Full;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            shell = new
            {
                type = "string",
                description = "Logical shell name: pwsh | powershell | cmd | gitbash | wsl. Defaults to pwsh.",
                @enum = new[] { "pwsh", "powershell", "cmd", "gitbash", "wsl" }
            },
            working_directory = new
            {
                type = "string",
                description = "Working directory for the new terminal. Must resolve inside the project root."
            }
        },
        additionalProperties = false
    });

    public Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        if (context.SandboxEnabled)
        {
            return Task.FromResult(Error(context, "sandbox_blocked", "open_terminal is disabled while the session sandbox is engaged."));
        }

        var args = JsonSerializer.Deserialize<OpenTerminalArguments>(
            context.ArgumentsJson,
            JsonSerialization.Options) ?? new OpenTerminalArguments(null, null);

        var workingDir = args.WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(workingDir)
            && !PathSafety.EnsureWithinRoot(context.ProjectRoot, workingDir!, out workingDir!))
        {
            return Task.FromResult(Error(context, "working_directory_outside_root", $"Working directory '{args.WorkingDirectory}' resolves outside the project root."));
        }

        var spawn = terminalManager.Spawn(new TerminalSpawnRequest(
            ProjectPath: context.ProjectRoot,
            Shell: args.Shell,
            WorkingDirectory: workingDir,
            Cols: 120,
            Rows: 30,
            EnvVars: null));

        var payload = JsonSerializer.Serialize(
            new OpenTerminalResult(spawn.TerminalId, spawn.Shell, spawn.Started, spawn.Error),
            JsonSerialization.Options);

        return Task.FromResult(new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: !spawn.Started));
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("open_terminal", context.CallId, payload, IsError: true);
    }

    private sealed record OpenTerminalArguments(
        [property: JsonPropertyName("shell")] string? Shell,
        [property: JsonPropertyName("working_directory")] string? WorkingDirectory);

    private sealed record OpenTerminalResult(
        [property: JsonPropertyName("terminal_id")] string TerminalId,
        [property: JsonPropertyName("shell")] string Shell,
        [property: JsonPropertyName("started")] bool Started,
        [property: JsonPropertyName("error")] string? Error);
}
