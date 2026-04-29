using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Modes;

/// <summary>
/// Spec §7 mode catalog. Owns CRUD over <see cref="ModeEntity"/> and seeds the four
/// built-in modes (Plan, Code, Debug, Ask) with the system prompts and tool whitelists
/// defined in spec §7.1. Built-in modes are never deletable but are upsertable so a
/// future migration can rewrite their prompts without losing user customizations.
/// </summary>
public sealed class ModesService(IDbContextFactory<NexCodeDbContext> dbContextFactory)
{
    private static readonly IReadOnlyList<BuiltInModeDefinition> BuiltIns =
    [
        new BuiltInModeDefinition(
            Name: "Plan",
            SystemPrompt:
                "You are NexCode in Plan Mode. Read the user's intent, gather context with read-only tools, " +
                "and produce a concise, ordered implementation plan with explicit steps and acceptance criteria. " +
                "Do not modify files, do not run shell commands, and do not make irreversible changes. " +
                "When the plan is ready, ask the user to confirm before execution.",
            Icon: "Map",
            AccentColor: "#4F8CFF",
            AllowedTools:
            [
                "read_file",
                "list_directory",
                "search_files",
                "git_status",
                "git_diff",
                "memory_read",
                "memory_list"
            ]),
        new BuiltInModeDefinition(
            Name: "Code",
            SystemPrompt:
                "You are NexCode in Code Mode. Implement the user's request directly. " +
                "Use read_file, write_file, create_file, delete_file, and execute_command as needed. " +
                "Prefer small, reversible changes; create checkpoints between logical steps. " +
                "After each meaningful edit, verify with the smallest possible build/test/lint command.",
            Icon: "Code",
            AccentColor: "#36C172",
            AllowedTools:
            [
                "read_file",
                "write_file",
                "create_file",
                "delete_file",
                "cut_paste_file",
                "list_directory",
                "search_files",
                "execute_command",
                "git_status",
                "git_diff",
                "git_revert",
                "memory_read",
                "memory_write",
                "memory_list"
            ]),
        new BuiltInModeDefinition(
            Name: "Debug",
            SystemPrompt:
                "You are NexCode in Debug Mode. Reproduce the failure, isolate the smallest hypothesis, " +
                "and verify it with execute_command (tests, lints, prints). Read-then-edit-then-verify; " +
                "do not guess. State each hypothesis, the experiment that confirmed it, and the minimal fix.",
            Icon: "Bug",
            AccentColor: "#F4A340",
            AllowedTools:
            [
                "read_file",
                "write_file",
                "list_directory",
                "search_files",
                "execute_command",
                "git_status",
                "git_diff",
                "git_revert",
                "memory_read",
                "memory_write",
                "memory_list"
            ]),
        new BuiltInModeDefinition(
            Name: "Ask",
            SystemPrompt:
                "You are NexCode in Ask Mode. Answer the user's question using only read-only tools. " +
                "Do not modify the workspace and do not run shell commands. " +
                "Prefer concise answers grounded in the actual code you read; cite file paths when helpful.",
            Icon: "Question",
            AccentColor: "#9B6BFF",
            AllowedTools:
            [
                "read_file",
                "list_directory",
                "search_files",
                "git_status",
                "git_diff",
                "memory_read",
                "memory_list"
            ])
    ];

    public async Task EnsureBuiltInsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var definition in BuiltIns)
        {
            var existing = await dbContext.Modes
                .SingleOrDefaultAsync(mode => mode.Name == definition.Name && mode.IsBuiltIn, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                dbContext.Modes.Add(new ModeEntity
                {
                    Id = Guid.NewGuid(),
                    Name = definition.Name,
                    SystemPrompt = definition.SystemPrompt,
                    Icon = definition.Icon,
                    AccentColor = definition.AccentColor,
                    IsBuiltIn = true,
                    AllowedToolsJson = JsonSerializer.Serialize(definition.AllowedTools, JsonSerialization.Options)
                });
            }
            else
            {
                existing.SystemPrompt = definition.SystemPrompt;
                existing.Icon = definition.Icon;
                existing.AccentColor = definition.AccentColor;
                existing.AllowedToolsJson = JsonSerializer.Serialize(definition.AllowedTools, JsonSerialization.Options);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ModeListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await dbContext.Modes
            .AsNoTracking()
            .OrderByDescending(mode => mode.IsBuiltIn)
            .ThenBy(mode => mode.Name)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = rows.Select(ToSummary).ToArray();
        return new ModeListResponse(summaries);
    }

    public async Task<ModeSummary> UpsertAsync(ModeUpsertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Mode name must be provided.", nameof(request));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        ModeEntity entity;
        if (request.Id is { } id)
        {
            entity = await dbContext.Modes.SingleOrDefaultAsync(mode => mode.Id == id, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"Mode '{id}' not found.");
        }
        else
        {
            entity = new ModeEntity { Id = Guid.NewGuid(), IsBuiltIn = false };
            dbContext.Modes.Add(entity);
        }

        entity.Name = request.Name.Trim();
        entity.SystemPrompt = request.SystemPrompt ?? string.Empty;
        entity.Icon = request.Icon;
        entity.AccentColor = request.AccentColor;
        entity.AllowedToolsJson = request.AllowedTools is { Length: > 0 }
            ? JsonSerializer.Serialize(request.AllowedTools, JsonSerialization.Options)
            : null;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSummary(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entity = await dbContext.Modes.SingleOrDefaultAsync(mode => mode.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        if (entity.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in modes cannot be deleted.");
        }

        dbContext.Modes.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static ModeSummary ToSummary(ModeEntity entity)
    {
        var allowedTools = ParseAllowedTools(entity.AllowedToolsJson);
        return new ModeSummary(
            Id: entity.Id,
            Name: entity.Name,
            SystemPrompt: entity.SystemPrompt,
            Icon: entity.Icon,
            AccentColor: entity.AccentColor,
            IsBuiltIn: entity.IsBuiltIn,
            AllowedTools: allowedTools);
    }

    private static string[] ParseAllowedTools(string? json)
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

    private sealed record BuiltInModeDefinition(
        string Name,
        string SystemPrompt,
        string? Icon,
        string? AccentColor,
        string[] AllowedTools);
}
