using System.Diagnostics;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexCode.Service.Git;

namespace NexCode.Service.Automations;

/// <summary>
/// Executes one <see cref="AutomationStep"/> against the helper's services. Each step shape
/// has a dedicated method; unknown shapes log a warning and no-op so an old persisted
/// definition cannot crash the engine.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutomationStepExecutor(
    IHttpClientFactory httpClientFactory,
    ICheckpointService checkpointService,
    ILogger<AutomationStepExecutor> logger)
{
    public Task ExecuteAsync(AutomationStep step, CancellationToken cancellationToken) =>
        step switch
        {
            RunCommandStep run => ExecuteRunCommandAsync(run, cancellationToken),
            SendNotificationStep notify => ExecuteSendNotificationAsync(notify),
            CallWebhookStep webhook => ExecuteCallWebhookAsync(webhook, cancellationToken),
            GitActionStep git => ExecuteGitActionAsync(git, cancellationToken),
            WaitStep wait => Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, wait.DurationMs)), cancellationToken),
            ConditionalStep conditional => ExecuteConditionalAsync(conditional, cancellationToken),
            RunSessionStep session => ExecuteRunSessionAsync(session),
            _ => LogUnknown(step)
        };

    private Task LogUnknown(AutomationStep step)
    {
        logger.LogWarning("Automation step {Kind} has no executor; skipping.", step.Kind);
        return Task.CompletedTask;
    }

    private async Task ExecuteRunCommandAsync(RunCommandStep step, CancellationToken cancellationToken)
    {
        var shell = string.IsNullOrWhiteSpace(step.Shell) ? "powershell" : step.Shell.ToLowerInvariant();
        var (executable, arguments) = ResolveShell(shell, step.Command);
        if (executable is null)
        {
            logger.LogWarning("Automation run_command unsupported shell '{Shell}'.", shell);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(step.WorkingDirectory)
                ? Environment.CurrentDirectory
                : step.WorkingDirectory!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) { logger.LogDebug("[automation:cmd] {Line}", e.Data); }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) { logger.LogDebug("[automation:cmd:err] {Line}", e.Data); }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timeout = TimeSpan.FromSeconds(Math.Clamp(step.TimeoutSeconds ?? 60, 1, 600));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }

    private Task ExecuteSendNotificationAsync(SendNotificationStep step)
    {
        // Spec §22.2: Windows AppNotifications require WinRT toast XML; the helper logs the
        // request as a portable fallback so headless deployments still observe the side
        // effect. The GUI shell is responsible for surfacing actual toasts.
        logger.LogInformation("[notification] {Title}: {Body}", step.Title, step.Body);
        return Task.CompletedTask;
    }

    private async Task ExecuteCallWebhookAsync(CallWebhookStep step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.Url) || !Uri.TryCreate(step.Url, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Automation call_webhook missing or invalid URL.");
            return;
        }

        var method = string.IsNullOrWhiteSpace(step.Method) ? "POST" : step.Method.ToUpperInvariant();
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (!string.IsNullOrEmpty(step.Body))
        {
            request.Content = new StringContent(step.Body, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(step.HeadersJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(step.HeadersJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        request.Headers.TryAddWithoutValidation(prop.Name, prop.Value.ToString());
                    }
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Automation call_webhook headers JSON invalid.");
            }
        }

        var client = httpClientFactory.CreateClient(nameof(AutomationStepExecutor));
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        logger.LogDebug(
            "Automation call_webhook {Method} {Url} -> {Status}",
            method,
            uri,
            (int)response.StatusCode);
    }

    private async Task ExecuteGitActionAsync(GitActionStep step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.ProjectPath) || string.IsNullOrWhiteSpace(step.Action))
        {
            logger.LogWarning("Automation git_action missing project_path or action.");
            return;
        }

        switch (step.Action.ToLowerInvariant())
        {
            case "status":
                await checkpointService.GetStatusAsync(step.ProjectPath, cancellationToken).ConfigureAwait(false);
                break;
            case "diff":
                await checkpointService
                    .GetDiffAsync(step.ProjectPath, step.Ref ?? "HEAD", string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "revert":
                if (!string.IsNullOrEmpty(step.Ref))
                {
                    await checkpointService.RevertAsync(step.ProjectPath, step.Ref, cancellationToken).ConfigureAwait(false);
                }
                break;
            default:
                logger.LogWarning("Automation git_action unknown action '{Action}'.", step.Action);
                break;
        }
    }

    private async Task ExecuteConditionalAsync(ConditionalStep step, CancellationToken cancellationToken)
    {
        // Minimal expression evaluator: empty / "false" / "0" -> else; everything else -> then.
        var condition = step.Expression?.Trim() ?? string.Empty;
        var truthy = !(string.IsNullOrEmpty(condition)
                       || string.Equals(condition, "false", StringComparison.OrdinalIgnoreCase)
                       || condition == "0");
        var branch = truthy ? step.ThenSteps : step.ElseSteps;
        foreach (var inner in branch ?? Array.Empty<AutomationStep>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteAsync(inner, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task ExecuteRunSessionAsync(RunSessionStep step)
    {
        // run_session orchestrates a full helper turn; the engine logs intent here and a
        // future slice will wire it to SessionTurnService once a service-side session can be
        // booted without requiring an active named-pipe client.
        logger.LogInformation(
            "[automation:run_session] project={Project} mode={Mode} prompt={Prompt}",
            step.ProjectPath,
            step.Mode ?? "code",
            step.Prompt);
        return Task.CompletedTask;
    }

    private static (string? Executable, string Arguments) ResolveShell(string shell, string command)
    {
        return shell switch
        {
            "powershell" or "pwsh" => ("powershell.exe", $"-NonInteractive -Command \"{command.Replace("\"", "\\\"")}\""),
            "cmd" => ("cmd.exe", $"/C {command}"),
            "bash" => ("bash.exe", $"-c \"{command.Replace("\"", "\\\"")}\""),
            "wsl" => ("wsl.exe", $"-- bash -c \"{command.Replace("\"", "\\\"")}\""),
            _ => (null, string.Empty)
        };
    }
}
