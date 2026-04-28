using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §5.4 / §6.2 <c>execute_command</c>: runs a single shell invocation in one of
/// the supported host shells, capturing stdout/stderr and the exit code. Sandboxed
/// sessions reject the call outright (the sandbox runs no executables). Always uses a
/// hard timeout that kills the entire process tree on expiry.
/// </summary>
public sealed class ExecuteCommandTool : ITool
{
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 120;

    public string Name => "execute_command";

    public string Description =>
        "Run a shell command (powershell, cmd, pwsh, bash, or wsl) inside the project root and capture stdout/stderr. Refused while the session sandbox is enabled.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Full;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The full command line to run inside the chosen shell." },
            shell = new
            {
                type = "string",
                @enum = new[] { "powershell", "cmd", "pwsh", "bash", "wsl" },
                @default = "powershell"
            },
            timeout_seconds = new { type = "integer", minimum = 1, maximum = MaxTimeoutSeconds, @default = DefaultTimeoutSeconds },
            working_directory = new { type = "string", description = "Defaults to the project root. Must resolve inside the project root." }
        },
        required = new[] { "command" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        if (context.SandboxEnabled)
        {
            return Error(context, "sandbox_blocked", "execute_command is disabled while the session sandbox is engaged.");
        }

        var arguments = JsonSerializer.Deserialize<ExecuteCommandArguments>(
                            context.ArgumentsJson,
                            JsonSerialization.Options)
                        ?? throw new InvalidOperationException("Missing arguments for execute_command.");

        if (string.IsNullOrWhiteSpace(arguments.Command))
        {
            return Error(context, "missing_command", "The 'command' argument is required.");
        }

        var shell = string.IsNullOrWhiteSpace(arguments.Shell) ? "powershell" : arguments.Shell!.ToLowerInvariant();
        var timeoutSeconds = Math.Clamp(arguments.TimeoutSeconds ?? DefaultTimeoutSeconds, 1, MaxTimeoutSeconds);

        string workingDirectory;
        if (string.IsNullOrWhiteSpace(arguments.WorkingDirectory))
        {
            workingDirectory = Path.GetFullPath(context.ProjectRoot);
        }
        else if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, arguments.WorkingDirectory!, out workingDirectory!))
        {
            return Error(context, "working_directory_outside_root", $"Working directory '{arguments.WorkingDirectory}' resolves outside the project root.");
        }

        if (!Directory.Exists(workingDirectory))
        {
            return Error(context, "working_directory_missing", $"Working directory '{workingDirectory}' does not exist.");
        }

        var (executable, processArguments) = ResolveShell(shell, arguments.Command);
        if (executable is null)
        {
            return Error(context, "unsupported_shell", $"Shell '{shell}' is not supported on this host.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = processArguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return Error(context, "process_start_failed", ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already exited or kill is not available; nothing else we can do.
            }
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // Best-effort.
            }
        }

        var exitCode = timedOut ? -1 : process.ExitCode;
        var payload = JsonSerializer.Serialize(
            new ExecuteCommandResult(exitCode, stdout.ToString(), stderr.ToString(), timedOut),
            JsonSerialization.Options);

        return new ToolOutcome(
            ToolName: Name,
            CallId: context.CallId,
            ResultJson: payload,
            IsError: timedOut || exitCode != 0);
    }

    private static (string? Executable, string Arguments) ResolveShell(string shell, string command)
    {
        return shell switch
        {
            "powershell" or "pwsh" => (ResolvePowerShell(), $"-NonInteractive -Command \"{EscapeQuotes(command)}\""),
            "cmd" => (FindOnPath("cmd.exe") ?? "cmd.exe", $"/C {command}"),
            "bash" => (ResolveBash(), $"-c \"{EscapeQuotes(command)}\""),
            "wsl" => (FindOnPath("wsl.exe") ?? "wsl.exe", $"-- bash -c \"{EscapeQuotes(command)}\""),
            _ => (null, string.Empty)
        };
    }

    private static string ResolvePowerShell()
    {
        return FindOnPath("pwsh.exe")
               ?? FindOnPath("powershell.exe")
               ?? "powershell.exe";
    }

    private static string ResolveBash()
    {
        var fromPath = FindOnPath("bash.exe");
        if (fromPath is not null)
        {
            return fromPath;
        }

        foreach (var candidate in new[]
                 {
                     @"C:\Program Files\Git\bin\bash.exe",
                     @"C:\Program Files (x86)\Git\bin\bash.exe"
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "bash.exe";
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var directory in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim(), fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string EscapeQuotes(string value) => value.Replace("\"", "\\\"");

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(
            new { error = code, message },
            JsonSerialization.Options);
        return new ToolOutcome(
            ToolName: "execute_command",
            CallId: context.CallId,
            ResultJson: payload,
            IsError: true);
    }

    private sealed record ExecuteCommandArguments(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("shell")] string? Shell,
        [property: JsonPropertyName("timeout_seconds")] int? TimeoutSeconds,
        [property: JsonPropertyName("working_directory")] string? WorkingDirectory);

    private sealed record ExecuteCommandResult(
        [property: JsonPropertyName("exit_code")] int ExitCode,
        [property: JsonPropertyName("stdout")] string Stdout,
        [property: JsonPropertyName("stderr")] string Stderr,
        [property: JsonPropertyName("timed_out")] bool TimedOut);
}
