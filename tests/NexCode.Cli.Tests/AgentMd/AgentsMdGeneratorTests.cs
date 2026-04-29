using NexCode.Cli.AgentMd;

namespace NexCode.Cli.Tests.AgentMd;

public sealed class AgentsMdGeneratorTests
{
    [Fact]
    public void ScanProject_DetectsLanguagesAndTopLevelLayout()
    {
        using var workspace = new TempProject();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tests"));
        File.WriteAllText(Path.Combine(workspace.Path, "src", "Program.cs"), "namespace X;");
        File.WriteAllText(Path.Combine(workspace.Path, "src", "util.ts"), "export const x = 1;");
        File.WriteAllText(Path.Combine(workspace.Path, "tests", "test_app.py"), "def test(): pass\n");
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "# project\n");

        var snapshot = AgentsMdGenerator.ScanProject(workspace.Path);

        Assert.Contains(snapshot.Languages, l => l.StartsWith("C#"));
        Assert.Contains(snapshot.Languages, l => l.StartsWith("TypeScript"));
        Assert.Contains(snapshot.Languages, l => l.StartsWith("Python"));
        Assert.Contains("src", snapshot.TopLevelDirectories);
        Assert.Contains("tests", snapshot.TopLevelDirectories);
        Assert.Contains("README.md", snapshot.TopLevelFiles);
    }

    [Fact]
    public async Task GenerateAsync_AssemblesSixSectionMarkdown()
    {
        using var workspace = new TempProject();
        File.WriteAllText(Path.Combine(workspace.Path, "Program.cs"), "namespace X;");
        var stub = new StubProvider(new AgentsMdProviderSummary(
            Purpose: "Demo project.",
            Architecture: "Single C# file.",
            Build: "dotnet build",
            Test: "dotnet test",
            Lint: "dotnet format",
            Conventions: "Spaces, not tabs."));
        var generator = new AgentsMdGenerator(stub);

        var content = await generator.GenerateAsync(workspace.Path, CancellationToken.None);

        Assert.Contains("# AGENTS.md", content);
        Assert.Contains("## Purpose", content);
        Assert.Contains("## Architecture", content);
        Assert.Contains("## Build", content);
        Assert.Contains("## Test", content);
        Assert.Contains("## Lint", content);
        Assert.Contains("## Conventions", content);
        Assert.Contains("Demo project.", content);
        Assert.Contains("dotnet build", content);
        Assert.Contains("Detected languages", content);
    }

    [Fact]
    public async Task WriteAsync_WritesAgentsMdToProjectRoot()
    {
        using var workspace = new TempProject();
        File.WriteAllText(Path.Combine(workspace.Path, "main.py"), "print('hi')\n");
        var stub = new StubProvider(new AgentsMdProviderSummary(
            Purpose: "p", Architecture: "a", Build: "b", Test: "t", Lint: "l", Conventions: "c"));
        var generator = new AgentsMdGenerator(stub);

        var path = await generator.WriteAsync(workspace.Path, CancellationToken.None);

        Assert.Equal(Path.Combine(workspace.Path, "AGENTS.md"), path);
        Assert.True(File.Exists(path));
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("# AGENTS.md", text);
    }

    [Fact]
    public async Task GenerateAsync_NonexistentRoot_Throws()
    {
        var stub = new StubProvider(new AgentsMdProviderSummary("p", "a", "b", "t", "l", "c"));
        var generator = new AgentsMdGenerator(stub);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            generator.GenerateAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), CancellationToken.None));
    }

    private sealed class StubProvider : IAgentsMdProviderClient
    {
        private readonly AgentsMdProviderSummary _summary;

        public StubProvider(AgentsMdProviderSummary summary)
        {
            _summary = summary;
        }

        public Task<AgentsMdProviderSummary> SummarizeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
        {
            return Task.FromResult(_summary);
        }
    }

    private sealed class TempProject : IDisposable
    {
        public string Path { get; }

        public TempProject()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexcode-agentsmd-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
