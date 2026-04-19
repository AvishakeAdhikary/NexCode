using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexCode.Data.Storage;

public sealed class NexCodeDbContextFactory : IDesignTimeDbContextFactory<NexCodeDbContext>
{
    public NexCodeDbContext CreateDbContext(string[] args)
    {
        SqlCipherBootstrapper.EnsureInitialized();

        var builder = new DbContextOptionsBuilder<NexCodeDbContext>();
        builder.UseSqlite("Data Source=nexcode.db");
        return new NexCodeDbContext(builder.Options);
    }
}
