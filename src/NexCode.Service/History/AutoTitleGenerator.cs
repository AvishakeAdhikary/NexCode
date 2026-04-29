using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Storage;
using NexCode.Service.Providers;
using NexCode.Shared.Contracts;

namespace NexCode.Service.History;

/// <summary>
/// Spec §25: after a session sees its first assistant turn, ask the active provider to
/// summarize the conversation in 5-7 words and persist it to <c>Sessions.Title</c>. Emits
/// <see cref="ServiceEventTypes.SessionTitleUpdated"/>. Best-effort: any provider failure
/// is logged and swallowed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutoTitleGenerator(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    IModelProviderRegistry providerRegistry,
    ProviderConfigurationService providerConfiguration,
    ServiceEventHub serviceEventHub,
    ILogger<AutoTitleGenerator> logger)
{
    private const string SystemPrompt =
        "Title this session in 5-7 words. Just the title, no quotes.";

    public async Task<string?> GenerateAndPersistAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) { return null; }
        if (!string.IsNullOrEmpty(session.Title)) { return session.Title; }

        var transcriptRows = await db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (transcriptRows.Count < 2) { return null; }

        var defaultKey = await providerConfiguration
            .GetDefaultProviderKeyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (defaultKey is null) { return null; }

        var configuration = await providerConfiguration
            .FindConfiguredAsync(defaultKey, cancellationToken)
            .ConfigureAwait(false);
        var provider = providerRegistry.FindByKey(defaultKey);
        if (configuration is null || provider is null) { return null; }

        var conversation = transcriptRows
            .Select(m => new ProviderConversationMessage(
                Role: ParseRole(m.Role),
                Content: m.Content))
            .ToList();

        var request = new ProviderTurnRequest(
            Configuration: configuration,
            ModelId: configuration.DefaultModelId,
            SystemPrompt: SystemPrompt,
            Conversation: conversation,
            Tools: Array.Empty<ProviderToolDescriptor>(),
            MaxOutputTokens: 32,
            Temperature: 0.3);

        var titleBuilder = new System.Text.StringBuilder();
        try
        {
            await foreach (var ev in provider.StreamTurnAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (ev is TextDeltaEvent delta)
                {
                    titleBuilder.Append(delta.Delta);
                }
                else if (ev is TurnCompletedEvent)
                {
                    break;
                }
                else if (ev is ProviderErrorEvent error)
                {
                    logger.LogWarning("AutoTitle provider error: {Code} {Message}", error.Code, error.Message);
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AutoTitle generation failed for session {SessionId}.", sessionId);
            return null;
        }

        var title = NormalizeTitle(titleBuilder.ToString());
        if (string.IsNullOrEmpty(title)) { return null; }

        session.Title = title;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        serviceEventHub.Publish(
            ServiceEventTypes.SessionTitleUpdated,
            new SessionTitleUpdatedEventPayload(sessionId, title));
        return title;
    }

    private static string NormalizeTitle(string raw)
    {
        var trimmed = raw.Trim();
        trimmed = trimmed.Trim('"', '\'', '.', ' ');
        if (trimmed.Length > 80)
        {
            trimmed = trimmed[..80];
        }

        return trimmed;
    }

    private static ProviderMessageRole ParseRole(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "assistant" => ProviderMessageRole.Assistant,
            "tool" => ProviderMessageRole.Tool,
            "system" => ProviderMessageRole.System,
            _ => ProviderMessageRole.User
        };
}
