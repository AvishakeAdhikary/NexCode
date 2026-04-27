using System.Text;
using Microsoft.Extensions.Logging;
using NexCode.Data.Repositories;
using NexCode.Shared.Contracts;

namespace NexCode.Service;

public sealed class SessionTurnService(
    ILogger<SessionTurnService> logger,
    ServiceEventHub serviceEventHub,
    ISessionRepository sessionRepository)
{
    public void StartTurn(SessionRuntimeState session, Guid assistantMessageId, string userContent)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessTurnAsync(session, assistantMessageId, userContent);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Simulated session turn failed for {SessionId}", session.SessionId);
                serviceEventHub.Publish(
                    ServiceEventTypes.Status,
                    new StatusEventPayload(
                        SessionId: session.SessionId,
                        Message: $"Turn failed: {ex.Message}",
                        Level: "error"));
                serviceEventHub.Publish(
                    ServiceEventTypes.SessionEnd,
                    new SessionEndEventPayload(
                        SessionId: session.SessionId,
                        Reason: "failed"));
            }
        });
    }

    public async Task ProcessTurnAsync(
        SessionRuntimeState session,
        Guid assistantMessageId,
        string userContent,
        CancellationToken cancellationToken = default)
    {
        serviceEventHub.Publish(
            ServiceEventTypes.SessionStart,
            new SessionStartEventPayload(
                SessionId: session.SessionId,
                Mode: session.Request.Mode.ToString(),
                Personality: "Default",
                ProjectPath: session.Request.ProjectPath,
                SandboxEnabled: session.Request.SandboxEnabled));
        serviceEventHub.Publish(
            ServiceEventTypes.Status,
            new StatusEventPayload(
                SessionId: session.SessionId,
                Message: "Building the first helper-backed runtime turn.",
                Level: "info"));

        var assistantResponse = BuildSimulatedAssistantResponse(session, userContent);
        var responseBuilder = new StringBuilder(assistantResponse.Length);

        foreach (var chunk in SplitIntoChunks(assistantResponse, 28))
        {
            cancellationToken.ThrowIfCancellationRequested();
            responseBuilder.Append(chunk);
            serviceEventHub.Publish(
                ServiceEventTypes.Token,
                new TokenEventPayload(
                    SessionId: session.SessionId,
                    Content: chunk));
            await Task.Delay(TimeSpan.FromMilliseconds(35), cancellationToken);
        }

        var finalResponse = responseBuilder.ToString();
        await sessionRepository.UpdateMessageContentAsync(
            assistantMessageId,
            finalResponse,
            cancellationToken);

        serviceEventHub.Publish(
            ServiceEventTypes.Status,
            new StatusEventPayload(
                SessionId: session.SessionId,
                Message: "Creating the simulated checkpoint card for this turn.",
                Level: "info"));

        var diffSummary = "Simulated checkpoint created for the first helper-backed runtime turn. No file edits were recorded in this foundation slice.";
        var checkpointId = await sessionRepository.CreateCheckpointAsync(
            session.SessionId,
            assistantMessageId,
            gitCommitHash: null,
            diffSummary,
            filesChanged: [],
            cancellationToken);

        serviceEventHub.Publish(
            ServiceEventTypes.Checkpoint,
            new CheckpointEventPayload(
                SessionId: session.SessionId,
                CheckpointId: checkpointId,
                GitCommitHash: null,
                DiffSummary: diffSummary,
                FilesChanged: []));
        serviceEventHub.Publish(
            ServiceEventTypes.Status,
            new StatusEventPayload(
                SessionId: session.SessionId,
                Message: "Turn completed.",
                Level: "success"));
        serviceEventHub.Publish(
            ServiceEventTypes.SessionEnd,
            new SessionEndEventPayload(
                SessionId: session.SessionId,
                Reason: "completed"));
    }

    private static string BuildSimulatedAssistantResponse(SessionRuntimeState session, string userContent)
    {
        var projectName = Path.GetFileName(
            session.Request.ProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return $"NexCode received your message for {projectName} in {session.Request.Mode} mode. " +
               $"This slice is the first helper-backed turn runtime, so the assistant is simulating streamed output while still persisting the user message, assistant response shell, and checkpoint artifact. " +
               $"Your latest request was: \"{userContent.Trim()}\". " +
               "The next implementation slices will swap this scripted response for the real provider and tool loop.";
    }

    private static IEnumerable<string> SplitIntoChunks(string content, int chunkLength)
    {
        for (var start = 0; start < content.Length; start += chunkLength)
        {
            yield return content.Substring(start, Math.Min(chunkLength, content.Length - start));
        }
    }
}
