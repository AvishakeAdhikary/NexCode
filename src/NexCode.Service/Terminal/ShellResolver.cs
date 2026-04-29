using System;
using System.IO;
using System.Runtime.Versioning;

namespace NexCode.Service.Terminal;

/// <summary>
/// Spec §6.1 shell resolution. Maps logical names ("pwsh", "powershell", "cmd",
/// "gitbash", "wsl") to a concrete executable + args array suitable for ConPTY launch.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShellResolver
{
    public static ResolvedShell Resolve(string? shell, string? customExecutablePath = null, string? wslDistribution = null)
    {
        if (!string.IsNullOrWhiteSpace(customExecutablePath) && File.Exists(customExecutablePath))
        {
            return new ResolvedShell(
                Logical: "custom",
                Executable: customExecutablePath!,
                Args: Array.Empty<string>(),
                Available: true);
        }

        var logical = (shell ?? "pwsh").ToLowerInvariant();
        return logical switch
        {
            "pwsh" => ResolvePwsh(),
            "powershell" => ResolveWindowsPowerShell(),
            "cmd" => ResolveCmd(),
            "gitbash" or "git-bash" or "bash" => ResolveGitBash(),
            "wsl" => ResolveWsl(wslDistribution),
            _ => new ResolvedShell(logical, string.Empty, Array.Empty<string>(), Available: false)
        };
    }

    private static ResolvedShell ResolvePwsh()
    {
        var fromPath = FindOnPath("pwsh.exe");
        if (fromPath is not null)
        {
            return new ResolvedShell("pwsh", fromPath, Array.Empty<string>(), Available: true);
        }

        var pf = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
        {
            var probe = Path.Combine(pf, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(probe))
            {
                return new ResolvedShell("pwsh", probe, Array.Empty<string>(), Available: true);
            }
        }

        return new ResolvedShell("pwsh", "pwsh.exe", Array.Empty<string>(), Available: false);
    }

    private static ResolvedShell ResolveWindowsPowerShell()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var probe = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(probe))
        {
            return new ResolvedShell("powershell", probe, Array.Empty<string>(), Available: true);
        }

        var fromPath = FindOnPath("powershell.exe");
        if (fromPath is not null)
        {
            return new ResolvedShell("powershell", fromPath, Array.Empty<string>(), Available: true);
        }

        return new ResolvedShell("powershell", "powershell.exe", Array.Empty<string>(), Available: false);
    }

    private static ResolvedShell ResolveCmd()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var probe = Path.Combine(system32, "cmd.exe");
        if (File.Exists(probe))
        {
            return new ResolvedShell("cmd", probe, Array.Empty<string>(), Available: true);
        }

        var fromPath = FindOnPath("cmd.exe");
        return new ResolvedShell("cmd", fromPath ?? "cmd.exe", Array.Empty<string>(), Available: fromPath is not null);
    }

    private static ResolvedShell ResolveGitBash()
    {
        var pf = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
        {
            var probe = Path.Combine(pf, "Git", "bin", "bash.exe");
            if (File.Exists(probe))
            {
                return new ResolvedShell("gitbash", probe, new[] { "--login", "-i" }, Available: true);
            }
        }

        var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrEmpty(pf86))
        {
            var probe = Path.Combine(pf86, "Git", "bin", "bash.exe");
            if (File.Exists(probe))
            {
                return new ResolvedShell("gitbash", probe, new[] { "--login", "-i" }, Available: true);
            }
        }

        var fromPath = FindOnPath("bash.exe");
        return new ResolvedShell("gitbash", fromPath ?? "bash.exe", new[] { "--login", "-i" }, Available: fromPath is not null);
    }

    private static ResolvedShell ResolveWsl(string? distribution)
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var probe = Path.Combine(system32, "wsl.exe");
        var executable = File.Exists(probe) ? probe : (FindOnPath("wsl.exe") ?? "wsl.exe");
        var args = string.IsNullOrWhiteSpace(distribution)
            ? Array.Empty<string>()
            : new[] { "--distribution", distribution! };
        return new ResolvedShell("wsl", executable, args, Available: File.Exists(executable) || executable == "wsl.exe");
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
}

public sealed record ResolvedShell(
    string Logical,
    string Executable,
    string[] Args,
    bool Available);
