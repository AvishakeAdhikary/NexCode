using Microsoft.EntityFrameworkCore;

namespace NexCode.Data.Storage;

public sealed class NexCodeDatabaseInitializer(IDbContextFactory<NexCodeDbContext> dbContextFactory)
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
