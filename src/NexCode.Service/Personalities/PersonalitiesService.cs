using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Personalities;

/// <summary>
/// Spec §8 personality catalog. Personalities are global by default and layer a tone +
/// verbosity prompt fragment on top of the active mode's system prompt. The five built-in
/// personalities (Default, Detailed, Concise, Mentor, Senior Dev) are seeded on startup and
/// never deletable, but their fragments are upserted so a future migration can rewrite them.
/// </summary>
public sealed class PersonalitiesService(IDbContextFactory<NexCodeDbContext> dbContextFactory)
{
    public const string ScopeGlobal = "global";
    public const string ScopeProject = "project";

    private static readonly IReadOnlyList<BuiltInPersonalityDefinition> BuiltIns =
    [
        new BuiltInPersonalityDefinition(
            Name: "Default",
            Description: "Balanced assistant tuned for general coding work.",
            SystemPromptFragment:
                "Be helpful, accurate, and pragmatic. Match the user's level of detail. " +
                "Prefer concrete code over commentary.",
            Tone: "neutral",
            Verbosity: "medium",
            IsDefault: true),
        new BuiltInPersonalityDefinition(
            Name: "Detailed",
            Description: "Thorough explanations with rationale and trade-offs.",
            SystemPromptFragment:
                "Explain your reasoning. State trade-offs, name the alternatives you rejected, " +
                "and surface assumptions. Cite file paths and line ranges when relevant.",
            Tone: "informative",
            Verbosity: "high",
            IsDefault: false),
        new BuiltInPersonalityDefinition(
            Name: "Concise",
            Description: "Minimal narration, code-first answers.",
            SystemPromptFragment:
                "Be terse. Skip preamble and recap. Lead with the patch or command. " +
                "Drop adjectives, drop apologies.",
            Tone: "direct",
            Verbosity: "low",
            IsDefault: false),
        new BuiltInPersonalityDefinition(
            Name: "Mentor",
            Description: "Teaches the user while solving the task.",
            SystemPromptFragment:
                "Act as a patient mentor. After each meaningful change, briefly explain what " +
                "you did and why, the way a senior would coach a teammate. Encourage learning.",
            Tone: "encouraging",
            Verbosity: "high",
            IsDefault: false),
        new BuiltInPersonalityDefinition(
            Name: "Senior Dev",
            Description: "Opinionated, terse, and architecture-aware.",
            SystemPromptFragment:
                "You are a senior staff engineer. Push back on weak requirements, propose " +
                "the simplest architecture that holds up under change, and call out hidden " +
                "long-term costs. Keep the tone professional and direct.",
            Tone: "opinionated",
            Verbosity: "medium",
            IsDefault: false)
    ];

    public async Task EnsureBuiltInsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var definition in BuiltIns)
        {
            var existing = await dbContext.Personalities
                .SingleOrDefaultAsync(
                    p => p.Name == definition.Name && p.Scope == ScopeGlobal,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                dbContext.Personalities.Add(new PersonalityEntity
                {
                    Id = Guid.NewGuid(),
                    Name = definition.Name,
                    Description = definition.Description,
                    SystemPromptFragment = definition.SystemPromptFragment,
                    Tone = definition.Tone,
                    Verbosity = definition.Verbosity,
                    IsDefault = definition.IsDefault,
                    Scope = ScopeGlobal,
                    ProjectId = null
                });
            }
            else
            {
                existing.Description = definition.Description;
                existing.SystemPromptFragment = definition.SystemPromptFragment;
                existing.Tone = definition.Tone;
                existing.Verbosity = definition.Verbosity;
                if (existing.IsDefault != definition.IsDefault && definition.IsDefault)
                {
                    existing.IsDefault = true;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersonalityListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await dbContext.Personalities
            .AsNoTracking()
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = rows.Select(ToSummary).ToArray();
        return new PersonalityListResponse(summaries);
    }

    public async Task<PersonalitySummary> UpsertAsync(
        PersonalityUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Personality name must be provided.", nameof(request));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        PersonalityEntity entity;
        if (request.Id is { } id)
        {
            entity = await dbContext.Personalities.SingleOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"Personality '{id}' not found.");
        }
        else
        {
            entity = new PersonalityEntity { Id = Guid.NewGuid() };
            dbContext.Personalities.Add(entity);
        }

        entity.Name = request.Name.Trim();
        entity.Description = request.Description ?? string.Empty;
        entity.SystemPromptFragment = request.SystemPromptFragment ?? string.Empty;
        entity.Tone = request.Tone ?? string.Empty;
        entity.Verbosity = request.Verbosity ?? string.Empty;
        entity.Scope = string.IsNullOrWhiteSpace(request.Scope) ? ScopeGlobal : request.Scope!.Trim().ToLowerInvariant();
        entity.ProjectId = entity.Scope == ScopeProject ? request.ProjectId : null;
        entity.IsDefault = request.IsDefault;

        if (request.IsDefault)
        {
            await dbContext.Personalities
                .Where(p => p.Id != entity.Id && p.IsDefault)
                .ForEachAsync(p => p.IsDefault = false, cancellationToken)
                .ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSummary(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entity = await dbContext.Personalities.SingleOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        if (BuiltIns.Any(builtIn =>
                string.Equals(builtIn.Name, entity.Name, StringComparison.OrdinalIgnoreCase)
                && entity.Scope == ScopeGlobal))
        {
            throw new InvalidOperationException("Built-in personalities cannot be deleted.");
        }

        dbContext.Personalities.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static PersonalitySummary ToSummary(PersonalityEntity entity) =>
        new(
            Id: entity.Id,
            Name: entity.Name,
            Description: entity.Description,
            SystemPromptFragment: entity.SystemPromptFragment,
            Tone: entity.Tone,
            Verbosity: entity.Verbosity,
            Scope: entity.Scope,
            ProjectId: entity.ProjectId,
            IsDefault: entity.IsDefault);

    private sealed record BuiltInPersonalityDefinition(
        string Name,
        string Description,
        string SystemPromptFragment,
        string Tone,
        string Verbosity,
        bool IsDefault);
}
