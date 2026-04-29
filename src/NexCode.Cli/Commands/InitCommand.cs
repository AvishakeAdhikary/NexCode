using System.Text;
using System.Text.Json;
using NexCode.Cli.AgentMd;
using NexCode.Shared.Contracts;
using NexCode.Shared.Ipc;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Cli.Commands;

/// <summary>
/// <c>nexcode /init</c> (spec §34, §40.2): generate or refresh <c>AGENTS.md</c> in the
/// current working directory by asking the configured provider for a 6-section summary.
/// </summary>
internal static class InitCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var projectRoot = Path.GetFullPath(IpcClient.GetOption(args, "--project") ?? Environment.CurrentDirectory);
        if (!Directory.Exists(projectRoot))
        {
            Console.Error.WriteLine($"Project root '{projectRoot}' does not exist.");
            return 1;
        }

        var pipeName = IpcClient.GetPipeName(args);
        var provider = new IpcAgentsMdProviderClient(pipeName, projectRoot);
        var generator = new AgentsMdGenerator(provider);

        try
        {
            var path = await generator.WriteAsync(projectRoot, cancellationToken);
            Console.WriteLine($"Wrote {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"/init failed: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>
/// IPC-backed provider client for <c>/init</c>: opens a session against the helper,
/// asks the LLM for a 6-section project summary, and parses the JSON reply.
/// On any failure the client falls back to a heuristic snapshot summary so a useful
/// AGENTS.md is still emitted.
/// </summary>
internal sealed class IpcAgentsMdProviderClient : IAgentsMdProviderClient
{
    private readonly string _pipeName;
    private readonly string _projectRoot;

    public IpcAgentsMdProviderClient(string pipeName, string projectRoot)
    {
        _pipeName = pipeName;
        _projectRoot = projectRoot;
    }

    public async Task<AgentsMdProviderSummary> SummarizeAsync(
        ProjectSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SummarizeViaProviderAsync(snapshot, cancellationToken);
        }
        catch (Exception)
        {
            return Heuristic(snapshot);
        }
    }

    private async Task<AgentsMdProviderSummary> SummarizeViaProviderAsync(
        ProjectSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var createResponse = await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.SessionCreate,
                new SessionCreateRequest(
                    _projectRoot,
                    SessionMode.Ask,
                    ExecutionMode.Local,
                    PermissionLevel.Default,
                    SandboxEnabled: true)),
            _pipeName,
            cancellationToken);
        if (createResponse.Error is not null)
        {
            throw new InvalidOperationException(createResponse.Error.Message);
        }

        var session = createResponse.DeserializeResult<SessionCreateResponse>()
            ?? throw new InvalidOperationException("session.create returned no payload.");

        var prompt = BuildPrompt(snapshot);
        await IpcClient.SendAsync(
            JsonRpcRequest.Create(
                IpcMethods.SessionSendMessage,
                new SessionSendMessageRequest(session.SessionId, prompt)),
            _pipeName, cancellationToken);

        var fullText = await DrainAsync(session.SessionId, cancellationToken);
        return TryParseProviderJson(fullText) ?? Heuristic(snapshot);
    }

    private async Task<string> DrainAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        long? sequence = null;
        var idle = 0;
        while (idle < 50 && !cancellationToken.IsCancellationRequested)
        {
            var poll = await IpcClient.SendAsync(
                JsonRpcRequest.Create(IpcMethods.ServicePollEvents, new ServiceEventsPollRequest(sequence)),
                _pipeName, cancellationToken);
            if (poll.Error is not null) break;
            var events = poll.DeserializeResult<ServiceEventsPollResponse>();
            if (events is null) break;
            sequence = events.LatestSequence;
            var any = false;
            var ended = false;
            foreach (var envelope in events.Events)
            {
                any = true;
                if (envelope.EventType == ServiceEventTypes.Token)
                {
                    var token = envelope.Payload.Deserialize<TokenEventPayload>(JsonSerialization.Options);
                    if (token is not null && token.SessionId == sessionId)
                    {
                        builder.Append(token.Content);
                    }
                }
                else if (envelope.EventType == ServiceEventTypes.SessionEnd)
                {
                    ended = true;
                }
            }
            if (ended) break;
            if (!any) { idle++; await Task.Delay(100, cancellationToken); }
            else idle = 0;
        }
        return builder.ToString();
    }

    private static string BuildPrompt(ProjectSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Produce an AGENTS.md project summary as a JSON object with keys:");
        sb.AppendLine("purpose, architecture, build, test, lint, conventions.");
        sb.AppendLine("Each value must be 1-3 sentences. Output ONLY the JSON object, no prose.");
        sb.AppendLine();
        sb.AppendLine($"Root: {snapshot.ProjectRoot}");
        sb.AppendLine($"Languages: {string.Join(", ", snapshot.Languages)}");
        sb.AppendLine($"Top-level directories: {string.Join(", ", snapshot.TopLevelDirectories)}");
        sb.AppendLine($"Top-level files: {string.Join(", ", snapshot.TopLevelFiles)}");
        return sb.ToString();
    }

    private static AgentsMdProviderSummary? TryParseProviderJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(content[start..(end + 1)]);
            var root = doc.RootElement;
            return new AgentsMdProviderSummary(
                Purpose: root.TryGetProperty("purpose", out var p) ? p.GetString() ?? "(unknown)" : "(unknown)",
                Architecture: root.TryGetProperty("architecture", out var a) ? a.GetString() ?? "(unknown)" : "(unknown)",
                Build: root.TryGetProperty("build", out var b) ? b.GetString() ?? "(unknown)" : "(unknown)",
                Test: root.TryGetProperty("test", out var t) ? t.GetString() ?? "(unknown)" : "(unknown)",
                Lint: root.TryGetProperty("lint", out var l) ? l.GetString() ?? "(unknown)" : "(unknown)",
                Conventions: root.TryGetProperty("conventions", out var c) ? c.GetString() ?? "(unknown)" : "(unknown)");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static AgentsMdProviderSummary Heuristic(ProjectSnapshot snapshot)
    {
        var langs = snapshot.Languages.Length == 0 ? "unknown" : string.Join(", ", snapshot.Languages);
        return new AgentsMdProviderSummary(
            Purpose: "TODO: describe the project's purpose.",
            Architecture: $"Top-level directories: {string.Join(", ", snapshot.TopLevelDirectories)}.",
            Build: "TODO: document the build commands (e.g. `dotnet build`, `npm install`).",
            Test: "TODO: document the test commands (e.g. `dotnet test`, `pytest`).",
            Lint: "TODO: document the lint commands (e.g. `dotnet format`, `eslint .`).",
            Conventions: $"Detected languages: {langs}. TODO: document coding conventions.");
    }
}
