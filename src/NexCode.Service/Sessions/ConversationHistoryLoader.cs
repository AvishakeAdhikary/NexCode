using Microsoft.EntityFrameworkCore;
using NexCode.Data.Storage;
using NexCode.Service.Providers;

namespace NexCode.Service.Sessions;

/// <summary>
/// Loads the prior turns of a session from the encrypted <c>Messages</c> table and projects
/// them into the provider-neutral <see cref="ProviderConversationMessage"/> form. The
/// caller is responsible for appending the *current* user message before invoking the model.
/// </summary>
public sealed class ConversationHistoryLoader(IDbContextFactory<NexCodeDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<ProviderConversationMessage>> LoadAsync(
        Guid sessionId,
        Guid? excludeMessageId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        var ordered = rows
            .Where(m => excludeMessageId is null || m.Id != excludeMessageId)
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var conversation = new List<ProviderConversationMessage>(ordered.Count);
        foreach (var message in ordered)
        {
            var role = message.Role.ToLowerInvariant() switch
            {
                "user" => ProviderMessageRole.User,
                "assistant" => ProviderMessageRole.Assistant,
                "tool" => ProviderMessageRole.Tool,
                _ => ProviderMessageRole.System
            };

            conversation.Add(new ProviderConversationMessage(role, message.Content));
        }

        return conversation;
    }
}
