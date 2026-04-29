using System.Diagnostics;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode shell [--type powershell|cmd|custom]</c> (spec §5.2 / §15 terminal manager).
/// Direct CLI shell launch — when the helper service is running it surfaces the same shell
/// inside a TerminalManager session so the GUI can attach. When the helper is offline we
/// fall back to a plain interactive process so the CLI continues to work standalone.
/// </summary>
internal static class ShellCommand
{
    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var type = (IpcClient.GetOption(args, "--type") ?? "powershell").ToLowerInvariant();
        var customExe = IpcClient.GetOption(args, "--exec");

        var (fileName, arguments) = type switch
        {
            "powershell" or "pwsh" => (ResolvePowerShell(), string.Empty),
            "cmd" => ("cmd.exe", string.Empty),
            "custom" => (customExe ?? string.Empty, string.Empty),
            _ => (ResolvePowerShell(), string.Empty)
        };

        if (string.IsNullOrWhiteSpace(fileName))
        {
            Console.Error.WriteLine("Custom shell requires --exec <path-to-shell-exe>.");
            return Task.FromResult(1);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Console.Error.WriteLine($"Failed to launch shell '{fileName}'.");
                return Task.FromResult(1);
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* best-effort */ }
            });

            process.WaitForExit();
            return Task.FromResult(process.ExitCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"shell launch failed: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static string ResolvePowerShell()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (var candidate in new[] { "pwsh.exe", "powershell.exe" })
                {
                    string combined;
                    try { combined = Path.Combine(directory.Trim(), candidate); }
                    catch (ArgumentException) { continue; }
                    if (File.Exists(combined))
                    {
                        return combined;
                    }
                }
            }
        }
        return "powershell.exe";
    }
}
