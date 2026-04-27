using Microsoft.Extensions.Logging;
using NexCode.Data.Repositories;
using NexCode.Service.Providers;
using NexCode.Shared.Contracts;

namespace NexCode.Service;

public sealed class SessionTurnService(
    ILogger<SessionTurnService> logger,
    ServiceEventHub serviceEventHub,
    ISessionRepository sessionRepository,
    ISessionResponseProvider responseProvider)
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
                logger.LogError(ex, "Provider-backed session turn failed for {SessionId}", session.SessionId);
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
                Message: "Starting provider-backed helper turn.",
                Level: "info"));

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in responseProvider.GenerateTurnAsync(session, userContent, cancellationToken))
        {
            switch (update)
            {
                case ProviderStatusUpdate statusUpdate:
                    serviceEventHub.Publish(
                        ServiceEventTypes.Status,
                        new StatusEventPayload(
                            SessionId: session.SessionId,
                            Message: statusUpdate.Message,
                            Level: statusUpdate.Level));
                    break;
                case ProviderToolCallUpdate toolCallUpdate:
                    serviceEventHub.Publish(
                        ServiceEventTypes.ToolCall,
                        new ToolCallEventPayload(
                            SessionId: session.SessionId,
                            ToolName: toolCallUpdate.ToolName,
                            ArgumentsJson: toolCallUpdate.ArgumentsJson,
                            CallId: toolCallUpdate.CallId));
                    break;
                case ProviderToolResultUpdate toolResultUpdate:
                    serviceEventHub.Publish(
                        ServiceEventTypes.ToolResult,
                        new ToolResultEventPayload(
                            SessionId: session.SessionId,
                            CallId: toolResultUpdate.CallId,
                            ResultJson: toolResultUpdate.ResultJson,
                            IsError: toolResultUpdate.IsError));
                    break;
                case ProviderTokenUpdate tokenUpdate:
                    responseBuilder.Append(tokenUpdate.Content);
                    serviceEventHub.Publish(
                        ServiceEventTypes.Token,
                        new TokenEventPayload(
                            SessionId: session.SessionId,
                            Content: tokenUpdate.Content));
                    break;
            }
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
                Message: "Creating the checkpoint card for this turn.",
                Level: "info"));

        var diffSummary = "Checkpoint created after the first provider-backed turn. No file edits were recorded in this foundation slice.";
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
}
