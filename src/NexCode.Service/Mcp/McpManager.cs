using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Mcp;

/// <summary>
/// Spec §17 manager. Owns the active <see cref="McpClient"/>s, persists configurations to
/// the encrypted <c>MCPServers</c> table, and exposes IPC-driven CRUD + tool calls.
/// </summary>
public sealed class McpManager
{
    private readonly IDbContextFactory<NexCodeDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceEventHub _eventHub;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpManager> _logger;
    private readonly ConcurrentDictionary<Guid, McpClient> _clients = new();

    public McpManager(
        IDbContextFactory<NexCodeDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ServiceEventHub eventHub,
        ILoggerFactory loggerFactory)
    {
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _eventHub = eventHub;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpManager>();
    }

    public async Task<McpListResponse> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.McpServers.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var summaries = rows
            .Select(row => new McpServerSummary(
                Id: row.Id,
                Name: row.Name,
                Type: row.Type,
                ConnectionConfigJson: row.ConnectionConfigJson,
                AutoConnect: row.AutoConnect,
                Status: _clients.TryGetValue(row.Id, out var client) ? client.Status : McpServerStatuses.Disconnected))
            .ToArray();
        return new McpListResponse(summaries);
    }

    public async Task<McpUpsertResponse> UpsertAsync(
        McpUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required.");
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        McpServerEntity? entity = null;
        var created = false;

        if (request.Id is { } id)
        {
            entity = await db.McpServers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        }
        if (entity is null)
        {
            entity = new McpServerEntity { Id = request.Id ?? Guid.NewGuid() };
            db.McpServers.Add(entity);
            created = true;
        }

        entity.Name = request.Name;
        entity.Type = string.IsNullOrWhiteSpace(request.TransportType) ? McpTransportTypes.Stdio : request.TransportType;
        entity.ConnectionConfigJson = string.IsNullOrWhiteSpace(request.ConnectionConfigJson) ? "{}" : request.ConnectionConfigJson;
        entity.AutoConnect = request.AutoConnect;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new McpUpsertResponse(entity.Id, created);
    }

    public async Task<bool> RemoveAsync(Guid serverId, CancellationToken cancellationToken)
    {
        if (_clients.TryRemove(serverId, out var existingClient))
        {
            try
            {
                await existingClient.DisconnectAsync().ConfigureAwait(false);
            }
            finally
            {
                await existingClient.DisposeAsync().ConfigureAwait(false);
            }
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.McpServers.FirstOrDefaultAsync(x => x.Id == serverId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        db.McpServers.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ConnectAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var entity = await LoadEntityAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            throw new InvalidOperationException($"MCP server {serverId} not found.");
        }

        if (_clients.TryGetValue(serverId, out var existing) && existing.Status == McpServerStatuses.Connected)
        {
            return;
        }

        if (existing is not null)
        {
            await existing.DisposeAsync().ConfigureAwait(false);
            _clients.TryRemove(serverId, out _);
        }

        var transport = CreateTransport(entity.Type);
        var client = new McpClient(
            entity.Id,
            entity.Name,
            transport,
            _eventHub,
            _loggerFactory.CreateLogger<McpClient>());
        _clients[serverId] = client;

        var config = ParseConfig(entity.ConnectionConfigJson);
        try
        {
            await client.ConnectAsync(config, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _clients.TryRemove(serverId, out _);
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<bool> DisconnectAsync(Guid serverId, CancellationToken cancellationToken)
    {
        if (!_clients.TryRemove(serverId, out var client))
        {
            return false;
        }

        try
        {
            await client.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        return true;
    }

    public async Task<McpCallToolResponse> CallToolAsync(
        Guid serverId,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        if (!_clients.TryGetValue(serverId, out var client))
        {
            await ConnectAsync(serverId, cancellationToken).ConfigureAwait(false);
            if (!_clients.TryGetValue(serverId, out client))
            {
                throw new InvalidOperationException($"MCP server {serverId} is not connected.");
            }
        }

        JsonElement args;
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            args = JsonDocument.Parse("{}").RootElement.Clone();
        }
        else
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            args = doc.RootElement.Clone();
        }

        try
        {
            var result = await client.CallToolAsync(toolName, args, cancellationToken).ConfigureAwait(false);
            var isError = result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("isError", out var e)
                && e.ValueKind == JsonValueKind.True;
            return new McpCallToolResponse(serverId, toolName, result.GetRawText(), isError);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP tool {Tool} failed on server {Server}.", toolName, serverId);
            var errorPayload = JsonSerializer.Serialize(new
            {
                error = "mcp_tool_failure",
                message = ex.Message,
            }, JsonSerialization.Options);
            return new McpCallToolResponse(serverId, toolName, errorPayload, IsError: true);
        }
    }

    public IReadOnlyList<McpToolDescriptor> GetAggregatedToolDescriptors()
    {
        var list = new List<McpToolDescriptor>();
        foreach (var client in _clients.Values)
        {
            list.AddRange(client.Tools);
        }
        return list;
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> GetAggregatedToolDescriptorsAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return GetAggregatedToolDescriptors();
    }

    public async Task ConnectAutoAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.McpServers
            .AsNoTracking()
            .Where(x => x.AutoConnect)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            try
            {
                await ConnectAsync(row.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-connect failed for MCP server {Server}.", row.Name);
            }
        }
    }

    public async Task ShutdownAllAsync()
    {
        foreach (var kvp in _clients.ToArray())
        {
            try
            {
                await kvp.Value.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
            finally
            {
                await kvp.Value.DisposeAsync().ConfigureAwait(false);
            }
        }
        _clients.Clear();
    }

    private async Task<McpServerEntity?> LoadEntityAsync(Guid serverId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.McpServers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serverId, cancellationToken)
            .ConfigureAwait(false);
    }

    private IMcpTransport CreateTransport(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            McpTransportTypes.StreamableHttp => new StreamableHttpMcpTransport(_httpClientFactory),
            _ => new StdioMcpTransport(),
        };
    }

    private static JsonElement ParseConfig(string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
        using var doc = JsonDocument.Parse(configJson);
        return doc.RootElement.Clone();
    }
}
