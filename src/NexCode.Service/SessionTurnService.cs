using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using NexCode.Data.Repositories;
using NexCode.Service.Git;
using NexCode.Service.Permissions;
using NexCode.Service.Providers;
using NexCode.Service.Sessions;
using NexCode.Service.Tools;
using NexCode.Shared.Contracts;
using NexCode.Shared.Models;

namespace NexCode.Service;

/// <summary>
/// Spec §5.3 agent loop hosted in <c>NexCode.Service</c> per AD-0009. Each turn:
///   1. resolves the active <see cref="IModelProvider"/> from session config,
///   2. builds a system prompt + provider conversation from durable history,
///   3. streams provider events, persists tool results back into the conversation,
///   4. emits IPC events (<c>token</c>, <c>tool_call</c>, <c>tool_result</c>, <c>status</c>),
///   5. commits a real LibGit2Sharp checkpoint at end of turn,
///   6. persists the assistant message + checkpoint row + emits <c>session_end</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionTurnService(
    ILogger<SessionTurnService> logger,
    ServiceEventHub serviceEventHub,
    ISessionRepository sessionRepository,
    IModelProviderRegistry providerRegistry,
    ProviderConfigurationService providerConfiguration,
    IToolRegistry toolRegistry,
    ConversationHistoryLoader historyLoader,
    ICheckpointService checkpointService)
{
    private const int MaxToolRoundTrips = 6;
    private const int DefaultMaxTokens = 4096;
    private const double DefaultTemperature = 0.7;

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
                logger.LogError(ex, "Session turn failed for {SessionId}", session.SessionId);
                serviceEventHub.Publish(
                    ServiceEventTypes.Status,
                    new StatusEventPayload(session.SessionId, $"Turn failed: {ex.Message}", "error"));
                serviceEventHub.Publish(
                    ServiceEventTypes.SessionEnd,
                    new SessionEndEventPayload(session.SessionId, "failed"));
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

        var providerConfig = await ResolveProviderConfigAsync(cancellationToken);
        if (providerConfig is null)
        {
            serviceEventHub.Publish(
                ServiceEventTypes.Status,
                new StatusEventPayload(
                    session.SessionId,
                    "No AI provider is configured. Open Settings → Providers and add an API key before sending messages.",
                    "warning"));
            await sessionRepository.UpdateMessageContentAsync(
                assistantMessageId,
                "No AI provider is configured. Add a provider in Settings → Providers and try again.",
                cancellationToken);
            serviceEventHub.Publish(
                ServiceEventTypes.SessionEnd,
                new SessionEndEventPayload(session.SessionId, "no_provider"));
            return;
        }

        var provider = providerRegistry.FindByKey(providerConfig.ProviderKey);
        if (provider is null)
        {
            serviceEventHub.Publish(
                ServiceEventTypes.Status,
                new StatusEventPayload(
                    session.SessionId,
                    $"Provider '{providerConfig.ProviderKey}' is configured but no adapter is registered.",
                    "error"));
            await sessionRepository.UpdateMessageContentAsync(
                assistantMessageId,
                $"Provider '{providerConfig.ProviderKey}' is configured but no adapter is loaded.",
                cancellationToken);
            serviceEventHub.Publish(
                ServiceEventTypes.SessionEnd,
                new SessionEndEventPayload(session.SessionId, "missing_provider_adapter"));
            return;
        }

        serviceEventHub.Publish(
            ServiceEventTypes.Status,
            new StatusEventPayload(session.SessionId, $"Streaming via {provider.DisplayName}.", "info"));

        var history = await historyLoader.LoadAsync(session.SessionId, assistantMessageId, cancellationToken);
        var conversation = new List<ProviderConversationMessage>(history)
        {
            new(ProviderMessageRole.User, userContent)
        };

        var systemPrompt = SystemPromptBuilder.Build(session.Request.Mode, session.Request.ProjectPath);
        var tools = toolRegistry.DescribeForModel().ToList();
        var permissionMode = session.Request.PermissionLevel == PermissionLevel.Full
            ? PermissionMode.Full
            : PermissionMode.Default;

        var responseBuilder = new StringBuilder();
        var aggregatedFilesChanged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var roundTrip = 0; roundTrip < MaxToolRoundTrips; roundTrip++)
        {
            var request = new ProviderTurnRequest(
                Configuration: providerConfig,
                ModelId: string.IsNullOrEmpty(providerConfig.DefaultModelId)
                    ? DefaultModelIdFor(providerConfig.ProviderKey)
                    : providerConfig.DefaultModelId,
                SystemPrompt: systemPrompt,
                Conversation: conversation,
                Tools: tools,
                MaxOutputTokens: DefaultMaxTokens,
                Temperature: DefaultTemperature);

            var pendingToolUses = new List<ProviderToolUseRecord>();
            var assistantTextThisRound = new StringBuilder();
            var turnFinishReason = "completed";
            var hadError = false;

            await foreach (var providerEvent in provider.StreamTurnAsync(request, cancellationToken))
            {
                switch (providerEvent)
                {
                    case TextDeltaEvent text:
                        responseBuilder.Append(text.Delta);
                        assistantTextThisRound.Append(text.Delta);
                        serviceEventHub.Publish(
                            ServiceEventTypes.Token,
                            new TokenEventPayload(session.SessionId, text.Delta));
                        break;

                    case ToolUseRequestedEvent toolUse:
                        pendingToolUses.Add(new ProviderToolUseRecord(
                            CallId: toolUse.CallId,
                            ToolName: toolUse.ToolName,
                            ArgumentsJson: toolUse.ArgumentsJson));
                        serviceEventHub.Publish(
                            ServiceEventTypes.ToolCall,
                            new ToolCallEventPayload(
                                SessionId: session.SessionId,
                                ToolName: toolUse.ToolName,
                                ArgumentsJson: toolUse.ArgumentsJson,
                                CallId: toolUse.CallId));
                        break;

                    case TurnCompletedEvent completed:
                        turnFinishReason = completed.FinishReason;
                        break;

                    case ProviderRetryEvent retry:
                        serviceEventHub.Publish(
                            ServiceEventTypes.ProviderRetry,
                            new ProviderRetryEventPayload(
                                SessionId: session.SessionId,
                                Attempt: retry.Attempt,
                                Reason: retry.Reason,
                                DelayMs: (int)retry.Delay.TotalMilliseconds));
                        break;

                    case ProviderErrorEvent error:
                        hadError = true;
                        serviceEventHub.Publish(
                            ServiceEventTypes.ProviderError,
                            new ProviderErrorEventPayload(
                                SessionId: session.SessionId,
                                Code: error.Code,
                                Message: error.Message,
                                Recoverable: error.Recoverable));
                        serviceEventHub.Publish(
                            ServiceEventTypes.Status,
                            new StatusEventPayload(
                                session.SessionId,
                                $"Provider error: {error.Message}",
                                "error"));
                        break;
                }
            }

            if (hadError)
            {
                break;
            }

            var assistantTurnContent = assistantTextThisRound.ToString();
            conversation.Add(new ProviderConversationMessage(
                Role: ProviderMessageRole.Assistant,
                Content: assistantTurnContent,
                ToolUses: pendingToolUses.Count > 0 ? pendingToolUses : null));

            if (pendingToolUses.Count == 0 || turnFinishReason == "stop" || turnFinishReason == "end_turn" || turnFinishReason == "completed")
            {
                if (pendingToolUses.Count == 0)
                {
                    break;
                }
            }

            var toolResultBatch = new List<ProviderToolResultRecord>();
            foreach (var toolUse in pendingToolUses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var invocationContext = new ToolInvocationContext(
                    Session: session,
                    ProjectRoot: session.Request.ProjectPath,
                    SandboxEnabled: session.Request.SandboxEnabled,
                    PermissionMode: permissionMode,
                    CallId: toolUse.CallId);

                var outcome = await toolRegistry.InvokeAsync(
                    invocationContext,
                    toolUse.ToolName,
                    toolUse.ArgumentsJson,
                    cancellationToken);

                serviceEventHub.Publish(
                    ServiceEventTypes.ToolResult,
                    new ToolResultEventPayload(
                        SessionId: session.SessionId,
                        CallId: outcome.CallId,
                        ResultJson: outcome.ResultJson,
                        IsError: outcome.IsError));

                if (outcome.FilesChanged is { Length: > 0 })
                {
                    foreach (var file in outcome.FilesChanged)
                    {
                        aggregatedFilesChanged.Add(file);
                    }
                }

                toolResultBatch.Add(new ProviderToolResultRecord(
                    CallId: outcome.CallId,
                    ResultJson: outcome.ResultJson,
                    IsError: outcome.IsError));
            }

            conversation.Add(new ProviderConversationMessage(
                Role: ProviderMessageRole.Tool,
                Content: string.Empty,
                ToolResults: toolResultBatch));
        }

        var finalResponse = responseBuilder.ToString();
        if (string.IsNullOrEmpty(finalResponse))
        {
            finalResponse = "(no response)";
        }

        await sessionRepository.UpdateMessageContentAsync(
            assistantMessageId,
            finalResponse,
            cancellationToken);

        serviceEventHub.Publish(
            ServiceEventTypes.Status,
            new StatusEventPayload(session.SessionId, "Creating the checkpoint card for this turn.", "info"));

        CheckpointResult? checkpoint = null;
        try
        {
            checkpoint = await checkpointService.CreateAsync(
                session.Request.ProjectPath,
                session.SessionId,
                turnNumber: 1,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Checkpoint creation failed for session {SessionId}", session.SessionId);
        }

        var diffSummary = checkpoint?.DiffSummary
            ?? "Checkpoint skipped: project root is not a git repository or the working tree is clean.";
        var filesChanged = checkpoint?.FilesChanged.ToArray()
            ?? aggregatedFilesChanged.ToArray();

        var checkpointId = await sessionRepository.CreateCheckpointAsync(
            session.SessionId,
            assistantMessageId,
            gitCommitHash: checkpoint?.CommitHash,
            diffSummary,
            filesChanged,
            cancellationToken);

        serviceEventHub.Publish(
            ServiceEventTypes.Checkpoint,
            new CheckpointEventPayload(
                SessionId: session.SessionId,
                CheckpointId: checkpointId,
                GitCommitHash: checkpoint?.CommitHash,
                DiffSummary: diffSummary,
                FilesChanged: filesChanged));
        serviceEventHub.Publish(
            ServiceEventTypes.Status,
            new StatusEventPayload(session.SessionId, "Turn completed.", "success"));
        serviceEventHub.Publish(
            ServiceEventTypes.SessionEnd,
            new SessionEndEventPayload(session.SessionId, "completed"));
    }

    private async Task<ProviderConfiguration?> ResolveProviderConfigAsync(CancellationToken cancellationToken)
    {
        var defaultKey = await providerConfiguration.GetDefaultProviderKeyAsync(cancellationToken);
        if (defaultKey is null)
        {
            return null;
        }

        return await providerConfiguration.FindConfiguredAsync(defaultKey, cancellationToken);
    }

    private static string DefaultModelIdFor(string providerKey) => providerKey switch
    {
        "anthropic" => "claude-sonnet-4-6",
        "openai" => "gpt-5.4",
        _ => string.Empty
    };
}
