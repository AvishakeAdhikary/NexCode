using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.History;

/// <summary>
/// Wraps a SQLite FTS5 virtual table over <c>Messages.content</c> for spec §25 history
/// search. The FTS index is created on demand, populated from existing rows, and kept in
/// sync via SQL triggers. Helpful list/export/archive/delete operations live here too.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HistorySearchService(
    IDbContextFactory<NexCodeDbContext> dbContextFactory,
    EncryptedConnectionFactory connectionFactory,
    ILogger<HistorySearchService> logger)
{
    private bool _initialized;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) { return; }

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) { return; }

            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE VIRTUAL TABLE IF NOT EXISTS MessagesFts USING fts5(
                        message_id UNINDEXED,
                        session_id UNINDEXED,
                        content,
                        tokenize='unicode61'
                    );
                    CREATE TRIGGER IF NOT EXISTS Messages_ai AFTER INSERT ON Messages BEGIN
                        INSERT INTO MessagesFts(message_id, session_id, content)
                        VALUES (new.Id, new.SessionId, new.Content);
                    END;
                    CREATE TRIGGER IF NOT EXISTS Messages_ad AFTER DELETE ON Messages BEGIN
                        DELETE FROM MessagesFts WHERE message_id = old.Id;
                    END;
                    CREATE TRIGGER IF NOT EXISTS Messages_au AFTER UPDATE ON Messages BEGIN
                        DELETE FROM MessagesFts WHERE message_id = old.Id;
                        INSERT INTO MessagesFts(message_id, session_id, content)
                        VALUES (new.Id, new.SessionId, new.Content);
                    END;
                """;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var backfill = conn.CreateCommand())
            {
                backfill.CommandText = """
                    INSERT INTO MessagesFts(message_id, session_id, content)
                    SELECT m.Id, m.SessionId, m.Content
                    FROM Messages m
                    LEFT JOIN MessagesFts f ON f.message_id = m.Id
                    WHERE f.message_id IS NULL;
                """;
                await backfill.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HistorySearchService failed to initialize FTS5 index.");
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<HistoryListResponse> ListAsync(
        HistoryListRequest? request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<SessionEntity> q = db.Sessions.AsNoTracking().OrderByDescending(s => s.UpdatedAt);
        var limit = Math.Clamp(request?.Limit ?? 100, 1, 1000);

        var sessions = await q.Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var counts = await db.Messages
            .AsNoTracking()
            .Where(m => sessionIds.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var projectMap = new Dictionary<Guid, string>();
        var projectIds = sessions
            .Where(s => s.ProjectId.HasValue)
            .Select(s => s.ProjectId!.Value)
            .Distinct()
            .ToList();
        if (projectIds.Count > 0)
        {
            var projects = await db.Projects
                .AsNoTracking()
                .Where(p => projectIds.Contains(p.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var project in projects)
            {
                projectMap[project.Id] = project.DirectoryPath;
            }
        }

        var summaries = sessions
            .Where(s => string.IsNullOrEmpty(request?.ProjectFilter)
                || (s.ProjectId.HasValue
                    && projectMap.TryGetValue(s.ProjectId.Value, out var path)
                    && path.Contains(request!.ProjectFilter!, StringComparison.OrdinalIgnoreCase)))
            .Select(s => new HistorySummary(
                SessionId: s.Id,
                Title: s.Title,
                ProjectPath: s.ProjectId.HasValue && projectMap.TryGetValue(s.ProjectId.Value, out var path)
                    ? path
                    : string.Empty,
                Mode: s.ExecutionMode.ToString(),
                ProviderKey: string.Empty,
                CreatedAt: s.CreatedAt,
                UpdatedAt: s.UpdatedAt,
                MessageCount: counts.GetValueOrDefault(s.Id)))
            .ToArray();

        return new HistoryListResponse(summaries);
    }

    public async Task<HistorySearchResponse> SearchAsync(
        HistorySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new HistorySearchResponse(Array.Empty<HistorySearchHit>());
        }

        var hits = new List<HistorySearchHit>();
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.message_id, f.session_id, snippet(MessagesFts, 2, '<<', '>>', '...', 12) AS snip,
                   m.CreatedAt, bm25(MessagesFts) AS score
            FROM MessagesFts f
            JOIN Messages m ON m.Id = f.message_id
            WHERE MessagesFts MATCH $q
            ORDER BY score
            LIMIT $limit;
        """;
        cmd.Parameters.AddWithValue("$q", request.Query);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(request.Limit, 1, 500));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var messageId = Guid.Parse(reader.GetString(0));
            var sessionId = Guid.Parse(reader.GetString(1));
            var snippet = reader.GetString(2);
            var createdAtRaw = reader.GetValue(3)?.ToString() ?? string.Empty;
            DateTimeOffset.TryParse(
                createdAtRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var createdAt);
            var score = reader.IsDBNull(4) ? 0d : reader.GetDouble(4);
            hits.Add(new HistorySearchHit(sessionId, messageId, snippet, createdAt, score));
        }

        return new HistorySearchResponse(hits.ToArray());
    }

    public async Task<bool> ArchiveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) { return false; }
        session.Title = string.IsNullOrEmpty(session.Title) ? "[archived]" : $"[archived] {session.Title}";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) { return false; }
        var messages = db.Messages.Where(m => m.SessionId == sessionId);
        db.Messages.RemoveRange(messages);
        db.Sessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<HistoryExportResponse?> ExportAsync(
        Guid sessionId,
        string format,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) { return null; }

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var normalizedFormat = string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase)
            ? "markdown"
            : "json";

        var content = normalizedFormat switch
        {
            "markdown" => RenderMarkdown(session, messages),
            _ => RenderJson(session, messages)
        };

        return new HistoryExportResponse(sessionId, normalizedFormat, content);
    }

    private static string RenderMarkdown(SessionEntity session, IReadOnlyList<MessageEntity> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {session.Title ?? session.Id.ToString()}");
        sb.AppendLine();
        foreach (var message in messages)
        {
            sb.AppendLine($"## {message.Role} — {message.CreatedAt:O}");
            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string RenderJson(SessionEntity session, IReadOnlyList<MessageEntity> messages)
    {
        var doc = new
        {
            session_id = session.Id,
            title = session.Title,
            created_at = session.CreatedAt,
            updated_at = session.UpdatedAt,
            messages = messages.Select(m => new
            {
                id = m.Id,
                role = m.Role,
                content = m.Content,
                created_at = m.CreatedAt
            }).ToArray()
        };
        return JsonSerializer.Serialize(doc, JsonSerialization.Options);
    }
}
