using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Service.Memory;

/// <summary>
/// Spec §9 memory engine. Owns durable global + project memory through <see cref="MemoryEntity"/>
/// and surfaces session-scoped memory from <see cref="SessionMemoryStore"/>. Implements the Free
/// tier auto-eviction caps (50 global, 20 per project) by deleting the least-recently-accessed
/// rows, and exposes a TF-IDF-based <see cref="RelevantAsync"/> for the agent loop's context
/// injection step.
/// </summary>
public sealed class MemoryEngine(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    SessionMemoryStore sessionMemoryStore,
    ServiceEventHub serviceEventHub)
{
    public const int FreeGlobalCap = 50;
    public const int FreeProjectCap = 20;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "your", "you",
        "are", "was", "were", "have", "has", "had", "but", "not", "all", "any",
        "can", "will", "would", "should", "could", "than", "then", "they", "their",
        "there", "what", "when", "where", "which", "who", "whom", "how", "why",
        "about", "also", "been", "being", "more", "most", "much", "some", "such",
        "only", "over", "under", "out", "off", "its", "his", "her", "him", "she",
        "our", "ours", "yours", "theirs", "its"
    };

    public async Task<MemorySummary[]> ListAsync(
        MemoryScope? scope,
        Guid? projectId,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        // Session scope is runtime-only and never persisted (AD-0004).
        if (scope == MemoryScope.Session)
        {
            if (sessionId is null)
            {
                return [];
            }

            return sessionMemoryStore.List(sessionId.Value)
                .Select(entry => ToSessionSummary(entry, sessionId.Value))
                .ToArray();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = dbContext.Memories.AsNoTracking().AsQueryable();
        if (scope is { } s)
        {
            query = query.Where(m => m.Scope == s);
        }
        else
        {
            // Default list excludes session-scoped rows (they shouldn't exist in DB but defensive guard).
            query = query.Where(m => m.Scope != MemoryScope.Session);
        }

        if (projectId is { } pid)
        {
            query = query.Where(m => m.ProjectId == pid || m.Scope == MemoryScope.Global);
        }

        var rows = await query
            .OrderByDescending(m => m.LastAccessedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToSummary).ToArray();
    }

    public async Task<MemorySummary?> ReadAsync(
        string key,
        MemoryScope scope,
        Guid? projectId,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (scope == MemoryScope.Session)
        {
            if (sessionId is null)
            {
                return null;
            }

            var entry = sessionMemoryStore.Read(sessionId.Value, key);
            return entry is null ? null : ToSessionSummary(entry, sessionId.Value);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = dbContext.Memories
            .Where(m => m.Key == key && m.Scope == scope);

        if (scope == MemoryScope.Project)
        {
            query = query.Where(m => m.ProjectId == projectId);
        }

        var entity = await query.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.LastAccessedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSummary(entity);
    }

    public async Task<MemorySummary> WriteAsync(
        string key,
        string value,
        MemoryScope scope,
        Guid? projectId,
        Guid? sessionId,
        string[]? tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (scope == MemoryScope.Session)
        {
            if (sessionId is null)
            {
                throw new InvalidOperationException("Session-scoped memory writes require a session id.");
            }

            var entry = sessionMemoryStore.Write(sessionId.Value, key, value, tags ?? []);
            PublishMemoryEvent(key, scope, "created");
            return ToSessionSummary(entry, sessionId.Value);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = dbContext.Memories.Where(m => m.Key == key && m.Scope == scope);
        if (scope == MemoryScope.Project)
        {
            query = query.Where(m => m.ProjectId == projectId);
        }
        else
        {
            query = query.Where(m => m.ProjectId == null);
        }

        var existing = await query.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        string action;

        if (existing is null)
        {
            existing = new MemoryEntity
            {
                Id = Guid.NewGuid(),
                Key = key,
                Scope = scope,
                ProjectId = scope == MemoryScope.Project ? projectId : null,
                CreatedAt = now
            };
            dbContext.Memories.Add(existing);
            action = "created";
        }
        else
        {
            action = "updated";
        }

        existing.Value = value;
        existing.LastAccessedAt = now;
        existing.TagsJson = tags is { Length: > 0 }
            ? JsonSerializer.Serialize(tags, JsonSerialization.Options)
            : null;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await EvictIfOverCapAsync(dbContext, scope, scope == MemoryScope.Project ? projectId : null, cancellationToken).ConfigureAwait(false);

        PublishMemoryEvent(key, scope, action);
        return ToSummary(existing);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await dbContext.Memories.SingleOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        dbContext.Memories.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        PublishMemoryEvent(entity.Key, entity.Scope, "deleted");
        return true;
    }

    /// <summary>
    /// TF-IDF cosine similarity over the durable (global + project) memory corpus. Returns the
    /// top-N most relevant entries. The agent loop calls this when assembling context for a
    /// new turn so that "remember that X" facts surface automatically.
    /// </summary>
    public async Task<MemorySummary[]> RelevantAsync(
        string queryText,
        Guid? projectId,
        int topN = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText) || topN <= 0)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = dbContext.Memories.AsNoTracking()
            .Where(m => m.Scope != MemoryScope.Session);

        query = projectId is { } pid
            ? query.Where(m => m.Scope == MemoryScope.Global || m.ProjectId == pid)
            : query.Where(m => m.Scope == MemoryScope.Global);

        var corpus = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (corpus.Length == 0)
        {
            return [];
        }

        var queryTokens = Tokenize(queryText);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        var docTokens = corpus.Select(item => Tokenize(item.Value)).ToArray();
        var docFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var doc in docTokens)
        {
            foreach (var distinct in doc.Keys)
            {
                docFrequency[distinct] = docFrequency.TryGetValue(distinct, out var count) ? count + 1 : 1;
            }
        }

        var docCount = docTokens.Length;
        var queryVector = BuildTfIdfVector(queryTokens, docFrequency, docCount);
        var scored = new List<(MemoryEntity Entity, double Score)>(corpus.Length);
        for (var i = 0; i < corpus.Length; i++)
        {
            var docVector = BuildTfIdfVector(docTokens[i], docFrequency, docCount);
            var score = CosineSimilarity(queryVector, docVector);
            if (score > 0)
            {
                scored.Add((corpus[i], score));
            }
        }

        return scored
            .OrderByDescending(item => item.Score)
            .Take(topN)
            .Select(item => ToSummary(item.Entity))
            .ToArray();
    }

    private async Task EvictIfOverCapAsync(
        NexCodeDbContext dbContext,
        MemoryScope scope,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (scope == MemoryScope.Session)
        {
            return;
        }

        var cap = scope == MemoryScope.Global ? FreeGlobalCap : FreeProjectCap;
        var query = dbContext.Memories.Where(m => m.Scope == scope);
        query = scope == MemoryScope.Project
            ? query.Where(m => m.ProjectId == projectId)
            : query.Where(m => m.ProjectId == null);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total <= cap)
        {
            return;
        }

        var overflow = total - cap;
        var victims = await query
            .OrderBy(m => m.LastAccessedAt)
            .Take(overflow)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (victims.Length == 0)
        {
            return;
        }

        dbContext.Memories.RemoveRange(victims);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var victim in victims)
        {
            PublishMemoryEvent(victim.Key, victim.Scope, "evicted");
        }
    }

    private void PublishMemoryEvent(string key, MemoryScope scope, string action)
    {
        serviceEventHub.Publish(
            ServiceEventTypes.MemoryUpdated,
            new MemoryUpdatedEventPayload(key, scope, action));
    }

    private static MemorySummary ToSummary(MemoryEntity entity)
    {
        var tags = ParseTags(entity.TagsJson);
        return new MemorySummary(
            Id: entity.Id,
            Key: entity.Key,
            Value: entity.Value,
            Scope: entity.Scope,
            ProjectId: entity.ProjectId,
            SessionId: entity.SessionId,
            Tags: tags,
            CreatedAt: entity.CreatedAt,
            LastAccessedAt: entity.LastAccessedAt);
    }

    private static MemorySummary ToSessionSummary(SessionMemoryEntry entry, Guid sessionId) =>
        new(
            Id: entry.Id,
            Key: entry.Key,
            Value: entry.Value,
            Scope: MemoryScope.Session,
            ProjectId: null,
            SessionId: sessionId,
            Tags: entry.Tags,
            CreatedAt: entry.CreatedAt,
            LastAccessedAt: entry.LastAccessedAt);

    private static string[] ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonSerialization.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, int> Tokenize(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return counts;
        }

        var span = text.AsSpan();
        var start = -1;
        for (var i = 0; i <= span.Length; i++)
        {
            var isBoundary = i == span.Length || !char.IsLetterOrDigit(span[i]);
            if (isBoundary)
            {
                if (start >= 0 && i - start >= 3)
                {
                    var token = span[start..i].ToString().ToLowerInvariant();
                    if (!Stopwords.Contains(token))
                    {
                        counts[token] = counts.TryGetValue(token, out var c) ? c + 1 : 1;
                    }
                }
                start = -1;
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        return counts;
    }

    private static Dictionary<string, double> BuildTfIdfVector(
        Dictionary<string, int> tokenCounts,
        Dictionary<string, int> docFrequency,
        int docCount)
    {
        var vector = new Dictionary<string, double>(tokenCounts.Count, StringComparer.Ordinal);
        var totalTerms = tokenCounts.Values.Sum();
        if (totalTerms == 0)
        {
            return vector;
        }

        foreach (var (term, count) in tokenCounts)
        {
            var tf = (double)count / totalTerms;
            var df = docFrequency.TryGetValue(term, out var docs) ? docs : 0;
            // smoothed inverse document frequency
            var idf = Math.Log((1.0 + docCount) / (1.0 + df)) + 1.0;
            vector[term] = tf * idf;
        }

        return vector;
    }

    private static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        double dot = 0;
        foreach (var (term, weight) in a)
        {
            if (b.TryGetValue(term, out var other))
            {
                dot += weight * other;
            }
        }

        var normA = Math.Sqrt(a.Values.Sum(v => v * v));
        var normB = Math.Sqrt(b.Values.Sum(v => v * v));
        if (normA == 0 || normB == 0)
        {
            return 0;
        }

        return dot / (normA * normB);
    }
}
