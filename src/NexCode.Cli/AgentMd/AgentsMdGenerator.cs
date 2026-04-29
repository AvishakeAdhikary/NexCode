using System.Text;

namespace NexCode.Cli.AgentMd;

/// <summary>
/// Spec §34 + §40.2 — composes <c>AGENTS.md</c> for a project root. Detects languages,
/// scans the top level, then asks an injected <see cref="IAgentsMdProviderClient"/> to
/// produce a six-section summary (purpose / architecture / build / test / lint /
/// conventions). The provider client is abstracted so tests can pass a stub.
/// </summary>
public sealed class AgentsMdGenerator
{
    private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".csproj"] = "C#",
        [".fs"] = "F#",
        [".vb"] = "VB.NET",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript (React)",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript (React)",
        [".py"] = "Python",
        [".rs"] = "Rust",
        [".go"] = "Go",
        [".java"] = "Java",
        [".kt"] = "Kotlin",
        [".swift"] = "Swift",
        [".rb"] = "Ruby",
        [".php"] = "PHP",
        [".cpp"] = "C++",
        [".cc"] = "C++",
        [".c"] = "C",
        [".h"] = "C/C++ header",
        [".sh"] = "Shell",
        [".ps1"] = "PowerShell",
        [".sql"] = "SQL"
    };

    private readonly IAgentsMdProviderClient _provider;

    public AgentsMdGenerator(IAgentsMdProviderClient provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Inspect the project, ask the provider for a summary, and return the generated Markdown.</summary>
    public async Task<string> GenerateAsync(string projectRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException($"Project root '{projectRoot}' does not exist.");
        }

        var snapshot = ScanProject(projectRoot);
        var providerSummary = await _provider.SummarizeAsync(snapshot, cancellationToken);

        return Compose(snapshot, providerSummary);
    }

    /// <summary>Generate and write <c>AGENTS.md</c> in <paramref name="projectRoot"/>; returns the file path.</summary>
    public async Task<string> WriteAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var content = await GenerateAsync(projectRoot, cancellationToken);
        var target = Path.Combine(projectRoot, "AGENTS.md");
        await File.WriteAllTextAsync(target, content, cancellationToken);
        return target;
    }

    public static ProjectSnapshot ScanProject(string projectRoot)
    {
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var topLevelDirs = new List<string>();
        var topLevelFiles = new List<string>();

        foreach (var entry in Directory.EnumerateFileSystemEntries(projectRoot))
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
            if (Directory.Exists(entry))
            {
                topLevelDirs.Add(name);
            }
            else
            {
                topLevelFiles.Add(name);
            }
        }

        foreach (var file in EnumerateFilesSafe(projectRoot))
        {
            var ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext)) continue;
            if (!ExtensionToLanguage.TryGetValue(ext, out var lang)) continue;
            languages[lang] = languages.TryGetValue(lang, out var current) ? current + 1 : 1;
        }

        var sortedLanguages = languages
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => $"{kvp.Key} ({kvp.Value} files)")
            .ToArray();

        topLevelDirs.Sort(StringComparer.OrdinalIgnoreCase);
        topLevelFiles.Sort(StringComparer.OrdinalIgnoreCase);

        return new ProjectSnapshot(
            ProjectRoot: Path.GetFullPath(projectRoot),
            Languages: sortedLanguages,
            TopLevelDirectories: topLevelDirs.ToArray(),
            TopLevelFiles: topLevelFiles.ToArray());
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] subdirs;
            string[] files;
            try
            {
                subdirs = Directory.GetDirectories(current);
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }
            foreach (var dir in subdirs)
            {
                var name = Path.GetFileName(dir);
                if (name is "bin" or "obj" or "node_modules" or "dist" or "build" or ".git") continue;
                stack.Push(dir);
            }
            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    public static string Compose(ProjectSnapshot snapshot, AgentsMdProviderSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AGENTS.md");
        builder.AppendLine();
        builder.AppendLine($"_Generated by NexCode for `{snapshot.ProjectRoot}`._");
        builder.AppendLine();
        builder.AppendLine("## Purpose");
        builder.AppendLine(summary.Purpose);
        builder.AppendLine();
        builder.AppendLine("## Architecture");
        builder.AppendLine(summary.Architecture);
        builder.AppendLine();
        builder.AppendLine("## Build");
        builder.AppendLine(summary.Build);
        builder.AppendLine();
        builder.AppendLine("## Test");
        builder.AppendLine(summary.Test);
        builder.AppendLine();
        builder.AppendLine("## Lint");
        builder.AppendLine(summary.Lint);
        builder.AppendLine();
        builder.AppendLine("## Conventions");
        builder.AppendLine(summary.Conventions);
        builder.AppendLine();
        builder.AppendLine("## Detected languages");
        if (snapshot.Languages.Length == 0)
        {
            builder.AppendLine("- (none detected)");
        }
        else
        {
            foreach (var lang in snapshot.Languages)
            {
                builder.AppendLine($"- {lang}");
            }
        }
        builder.AppendLine();
        builder.AppendLine("## Top-level layout");
        foreach (var dir in snapshot.TopLevelDirectories)
        {
            builder.AppendLine($"- `{dir}/`");
        }
        foreach (var file in snapshot.TopLevelFiles)
        {
            builder.AppendLine($"- `{file}`");
        }
        return builder.ToString();
    }
}

public sealed record ProjectSnapshot(
    string ProjectRoot,
    string[] Languages,
    string[] TopLevelDirectories,
    string[] TopLevelFiles);

public sealed record AgentsMdProviderSummary(
    string Purpose,
    string Architecture,
    string Build,
    string Test,
    string Lint,
    string Conventions);

public interface IAgentsMdProviderClient
{
    Task<AgentsMdProviderSummary> SummarizeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken);
}
