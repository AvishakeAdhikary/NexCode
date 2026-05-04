using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace NexCode.Data.Storage;

public sealed class NexCodeDatabaseInitializer(IDbContextFactory<NexCodeDbContext> dbContextFactory)
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26 && TryQuarantinePlaintextDatabase(dbContext))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Detects a pre-encryption plaintext database left behind by an earlier build, renames
    /// it to a <c>.pre-encryption.bak</c> sidecar, and lets <c>EnsureCreated</c> recreate a
    /// proper SQLCipher-encrypted database in its place. Returns <c>true</c> if quarantine
    /// happened so the caller can retry. The data is preserved on disk; it is not deleted.
    /// </summary>
    private static bool TryQuarantinePlaintextDatabase(NexCodeDbContext dbContext)
    {
        var connectionString = dbContext.Database.GetDbConnection().ConnectionString;
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dbPath = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return false;
        }

        var quarantine = dbPath + ".pre-encryption.bak";
        if (File.Exists(quarantine))
        {
            quarantine = $"{dbPath}.pre-encryption.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
        }

        File.Move(dbPath, quarantine);
        foreach (var sidecar in new[] { dbPath + "-shm", dbPath + "-wal" })
        {
            if (File.Exists(sidecar))
            {
                File.Move(sidecar, sidecar + ".pre-encryption.bak", overwrite: true);
            }
        }
        return true;
    }
}
