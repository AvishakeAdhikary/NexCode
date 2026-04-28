using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexCode.Data.Storage;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling only. The runtime path goes through
/// <c>ServiceCollectionExtensions.AddNexCodeData</c>, which builds the connection string,
/// resolves an <see cref="IDatabaseKeyProvider"/>, and registers <see cref="SqlCipherKeyInterceptor"/>.
///
/// Because <c>dotnet ef</c> can't resolve the production key provider (no DI), this factory
/// uses a deliberately keyed transient database so migrations succeed without ever creating
/// or touching the user's encrypted database. Production callers <b>must not</b> use this
/// factory at runtime.
/// </summary>
public sealed class NexCodeDbContextFactory : IDesignTimeDbContextFactory<NexCodeDbContext>
{
    public NexCodeDbContext CreateDbContext(string[] args)
    {
        SqlCipherBootstrapper.EnsureInitialized();

        var designTimeKey = new FixedKeyDatabaseKeyProvider(new byte[]
        {
            0xDE, 0x51, 0x67, 0xCE, 0xDE, 0x51, 0x67, 0xCE,
            0xDE, 0x51, 0x67, 0xCE, 0xDE, 0x51, 0x67, 0xCE,
            0xDE, 0x51, 0x67, 0xCE, 0xDE, 0x51, 0x67, 0xCE,
            0xDE, 0x51, 0x67, 0xCE, 0xDE, 0x51, 0x67, 0xCE
        });

        var factory = new EncryptedConnectionFactory(designTimeKey, "nexcode-design.db");
        var builder = new DbContextOptionsBuilder<NexCodeDbContext>();
        builder.UseSqlite(factory.ConnectionString);
        builder.AddInterceptors(new SqlCipherKeyInterceptor(designTimeKey));
        return new NexCodeDbContext(builder.Options);
    }
}
