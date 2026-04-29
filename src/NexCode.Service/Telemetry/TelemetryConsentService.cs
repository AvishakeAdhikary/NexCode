using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;

namespace NexCode.Service.Telemetry;

/// <summary>
/// Spec §30: persist user telemetry consent. The first-login UX banner lives in the GUI; the
/// helper just owns the durable state. <see cref="GetAsync"/> is read-mostly and assumes a
/// single primary user (matching existing <c>UserEntity</c> semantics).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TelemetryConsentService(IDbContextFactory<NexCodeDbContext> dbContextFactory)
{
    public async Task<bool> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return user?.TelemetryEnabled ?? false;
    }

    public async Task<bool> SetAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = "local@nexcode.local",
                TelemetryEnabled = enabled
            };
            db.Users.Add(user);
        }
        else
        {
            user.TelemetryEnabled = enabled;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return enabled;
    }
}
