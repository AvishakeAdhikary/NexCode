using System.Text;
using System.Text.Json;
using NexCode.Service;
using NexCode.Service.Permissions;
using NexCode.Service.Tools;
using NexCode.Service.Tools.Implementations;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Cli.Tests.Tools;

public sealed class BuiltInToolTests
{
    [Fact]
    public async Task ReadFileTool_ReadsContent_FromProjectRoot()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.WriteFile("notes.txt", "hello world");
        var tool = new ReadFileTool();

        var outcome = await tool.ExecuteAsync(
            workspace.BuildContext("call", JsonSerializer.Serialize(new { path = "notes.txt" })),
            CancellationToken.None);

        Assert.False(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("hello world", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal(11, doc.RootElement.GetProperty("size").GetInt64());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ReadFileTool_RejectsPathOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var tool = new ReadFileTool();
        var args = JsonSerializer.Serialize(new { path = "..\\escape.txt" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("path_outside_root", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WriteFileTool_WritesContent_AndReportsBytes()
    {
        using var workspace = new TempWorkspace();
        var tool = new WriteFileTool();
        var args = JsonSerializer.Serialize(new { path = "out/sub.txt", content = "abc" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.NotNull(outcome.FilesChanged);
        var written = File.ReadAllText(Path.Combine(workspace.Root, "out", "sub.txt"));
        Assert.Equal("abc", written);
    }

    [Fact]
    public async Task WriteFileTool_RejectsPathOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var tool = new WriteFileTool();
        var args = JsonSerializer.Serialize(new { path = "..\\evil.txt", content = "x" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
    }

    [Fact]
    public async Task CreateFileTool_CreatesNewFile()
    {
        using var workspace = new TempWorkspace();
        var tool = new CreateFileTool();
        var args = JsonSerializer.Serialize(new { path = "new.txt", content = "fresh" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.True(File.Exists(Path.Combine(workspace.Root, "new.txt")));
    }

    [Fact]
    public async Task CreateFileTool_FailsIfExists()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("dup.txt", "x");
        var tool = new CreateFileTool();
        var args = JsonSerializer.Serialize(new { path = "dup.txt" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("file_exists", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DeleteFileTool_RemovesFile()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.WriteFile("kill.txt", "bye");
        var tool = new DeleteFileTool();
        var args = JsonSerializer.Serialize(new { path = "kill.txt" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DeleteFileTool_RejectsPathOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var tool = new DeleteFileTool();
        var args = JsonSerializer.Serialize(new { path = "..\\elsewhere.txt" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
    }

    [Fact]
    public async Task ListDirectoryTool_ListsEntries()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("a.txt", "1");
        workspace.WriteFile("b.txt", "2");
        Directory.CreateDirectory(Path.Combine(workspace.Root, "child"));
        var tool = new ListDirectoryTool();
        var args = JsonSerializer.Serialize(new { path = "." });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal(3, doc.RootElement.GetProperty("entry_count").GetInt32());
    }

    [Fact]
    public async Task ListDirectoryTool_RejectsPathOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var tool = new ListDirectoryTool();
        var args = JsonSerializer.Serialize(new { path = "..\\.." });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
    }

    [Fact]
    public async Task SearchFilesTool_FindsLiteralMatch()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("alpha.txt", "needle is here\nbut not here\n");
        workspace.WriteFile("beta.txt", "no match in this one\n");
        var tool = new SearchFilesTool();
        var args = JsonSerializer.Serialize(new { pattern = "needle" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        var matches = doc.RootElement.GetProperty("matches").EnumerateArray().ToArray();
        Assert.Single(matches);
        Assert.Equal(1, matches[0].GetProperty("line").GetInt32());
        Assert.Contains("needle", matches[0].GetProperty("preview").GetString());
    }

    [Fact]
    public async Task SearchFilesTool_FailsOnMissingPattern()
    {
        using var workspace = new TempWorkspace();
        var tool = new SearchFilesTool();
        var args = JsonSerializer.Serialize(new { pattern = "" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
    }

    [Fact]
    public async Task ExecuteCommandTool_RefusesWhenSandboxed()
    {
        using var workspace = new TempWorkspace(sandboxed: true);
        var tool = new ExecuteCommandTool();
        var args = JsonSerializer.Serialize(new { command = "echo hello" });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal("sandbox_blocked", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExecuteCommandTool_BenignCmdEcho_ReturnsZeroExitCode()
    {
        using var workspace = new TempWorkspace();
        var tool = new ExecuteCommandTool();
        var args = JsonSerializer.Serialize(new
        {
            command = "echo hello",
            shell = "cmd",
            timeout_seconds = 15
        });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal(0, doc.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Contains("hello", doc.RootElement.GetProperty("stdout").GetString());
        Assert.False(doc.RootElement.GetProperty("timed_out").GetBoolean());
    }

    [Fact]
    public async Task ExecuteCommandTool_RejectsWorkingDirectoryOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var tool = new ExecuteCommandTool();
        var args = JsonSerializer.Serialize(new
        {
            command = "echo hello",
            shell = "cmd",
            working_directory = "..\\.."
        });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
    }

    [Fact]
    public async Task CutPasteFileTool_SameFileMove_UpdatesContent_AndLeavesNoTempArtifacts()
    {
        using var workspace = new TempWorkspace();
        var content = "line1\nline2\nline3\nline4\nline5";
        var path = workspace.WriteFile("doc.txt", content);
        var tool = new CutPasteFileTool();
        var args = JsonSerializer.Serialize(new
        {
            source_file = "doc.txt",
            source_start_line = 2,
            source_end_line = 3,
            destination_file = "doc.txt",
            destination_insert_line = 5
        });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        var directory = Path.GetDirectoryName(path)!;
        Assert.False(File.Exists(Path.Combine(directory, "doc.txt.nexcode_tmp")));
        Assert.False(File.Exists(Path.Combine(directory, "doc.txt.nexcode_bak")));

        using var doc = JsonDocument.Parse(outcome.ResultJson);
        Assert.Equal(2, doc.RootElement.GetProperty("lines_moved").GetInt32());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("unified_diff").GetString()));
    }

    [Fact]
    public async Task CutPasteFileTool_AcrossFiles_LeavesNoTempArtifacts()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("from.txt", "alpha\nbeta\ngamma\ndelta");
        workspace.WriteFile("to.txt", "one\ntwo\nthree");
        var tool = new CutPasteFileTool();
        var args = JsonSerializer.Serialize(new
        {
            source_file = "from.txt",
            source_start_line = 2,
            source_end_line = 3,
            destination_file = "to.txt",
            destination_insert_line = 2
        });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.False(File.Exists(Path.Combine(workspace.Root, "from.txt.nexcode_tmp")));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "to.txt.nexcode_tmp")));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "from.txt.nexcode_bak")));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "to.txt.nexcode_bak")));

        var fromAfter = File.ReadAllText(Path.Combine(workspace.Root, "from.txt"));
        var toAfter = File.ReadAllText(Path.Combine(workspace.Root, "to.txt"));
        Assert.DoesNotContain("beta", fromAfter);
        Assert.Contains("beta", toAfter);
    }

    [Fact]
    public async Task CutPasteFileTool_RejectsPathOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("here.txt", "a\nb\nc");
        var tool = new CutPasteFileTool();
        var args = JsonSerializer.Serialize(new
        {
            source_file = "..\\escape.txt",
            source_start_line = 1,
            source_end_line = 1,
            destination_file = "here.txt",
            destination_insert_line = 1
        });

        var outcome = await tool.ExecuteAsync(workspace.BuildContext("call", args), CancellationToken.None);

        Assert.True(outcome.IsError);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }
        public bool Sandboxed { get; }

        public TempWorkspace(bool sandboxed = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "nexcode-tools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Sandboxed = sandboxed;
        }

        public string WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(Root, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return fullPath;
        }

        public ToolInvocationContext BuildContext(string callId, string argumentsJson)
        {
            var session = new SessionRuntimeState(
                SessionId: Guid.NewGuid(),
                Request: new SessionCreateRequest(
                    ProjectPath: Root,
                    Mode: SessionMode.Code,
                    ExecutionMode: ExecutionMode.Local,
                    PermissionLevel: PermissionLevel.Default,
                    SandboxEnabled: Sandboxed),
                CreatedAt: DateTimeOffset.UtcNow);

            return new ToolInvocationContext(
                Session: session,
                ProjectRoot: Root,
                SandboxEnabled: Sandboxed,
                PermissionMode: PermissionMode.Default,
                CallId: callId,
                ArgumentsJson: argumentsJson);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; tests should not fail because of leftover temp files.
            }
        }
    }
}
