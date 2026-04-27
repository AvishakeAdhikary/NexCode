using System.Text.Json;
using NexCode.Service.Tools;
using NexCode.Shared.Json;

namespace NexCode.Service.Providers;

public sealed class WorkspaceAwareSessionResponseProvider(SessionToolExecutor toolExecutor) : ISessionResponseProvider
{
    public async IAsyncEnumerable<ProviderTurnUpdate> GenerateTurnAsync(
        SessionRuntimeState session,
        string userContent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ProviderStatusUpdate(
            Message: "Inspecting workspace context for this turn.",
            Level: "info");

        var callId = $"tool-{Guid.NewGuid():N}";
        var toolArgumentsJson = JsonSerializer.Serialize(
            new
            {
                path = session.Request.ProjectPath,
                maxEntries = 6
            },
            JsonSerialization.Options);
        yield return new ProviderToolCallUpdate(
            ToolName: "list_directory",
            ArgumentsJson: toolArgumentsJson,
            CallId: callId);

        var toolResult = await toolExecutor.ExecuteAsync(
            session,
            "list_directory",
            toolArgumentsJson,
            cancellationToken);
        yield return new ProviderToolResultUpdate(
            CallId: callId,
            ResultJson: toolResult.ResultJson,
            IsError: toolResult.IsError);

        yield return new ProviderStatusUpdate(
            Message: toolResult.IsError
                ? "Workspace inspection returned a warning. Streaming the turn response anyway."
                : "Workspace inspection completed. Streaming the provider response.",
            Level: toolResult.IsError ? "warning" : "info");

        var assistantResponse = BuildAssistantResponse(
            session,
            userContent,
            toolResult.ResultJson,
            toolResult.IsError);

        foreach (var chunk in SplitIntoChunks(assistantResponse, 28))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ProviderTokenUpdate(chunk);
            await Task.Delay(TimeSpan.FromMilliseconds(35), cancellationToken);
        }
    }

    private static string BuildAssistantResponse(
        SessionRuntimeState session,
        string userContent,
        string toolResultJson,
        bool toolErrored)
    {
        var projectName = Path.GetFileName(
            session.Request.ProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (toolErrored)
        {
            return $"NexCode started a helper-backed turn for {projectName} in {session.Request.Mode} mode. " +
                   $"The provider abstraction is now active, and the first built-in tool path returned a warning. " +
                   $"Your latest request was: \"{userContent.Trim()}\". " +
                   "The assistant continued without workspace entries so the event and persistence flow could still complete.";
        }

        using var document = JsonDocument.Parse(toolResultJson);
        var root = document.RootElement;
        var entryCount = root.GetProperty("entryCount").GetInt32();
        var entries = root.GetProperty("entries")
            .EnumerateArray()
            .Select(item => $"{item.GetProperty("name").GetString()} ({item.GetProperty("entryType").GetString()})")
            .ToArray();
        var listedEntries = entries.Length == 0
            ? "no visible entries"
            : string.Join(", ", entries);

        return $"NexCode started a helper-backed turn for {projectName} in {session.Request.Mode} mode. " +
               $"This time the response came through the new provider abstraction, and the assistant used the first built-in tool path to inspect the active project directory. " +
               $"The workspace root currently exposed {entryCount} item(s); sampled entries: {listedEntries}. " +
               $"Your latest request was: \"{userContent.Trim()}\". " +
               "The next slices will replace this local provider with real external model execution while keeping the same streamed event contracts.";
    }

    private static IEnumerable<string> SplitIntoChunks(string content, int chunkLength)
    {
        for (var start = 0; start < content.Length; start += chunkLength)
        {
            yield return content.Substring(start, Math.Min(chunkLength, content.Length - start));
        }
    }
}
