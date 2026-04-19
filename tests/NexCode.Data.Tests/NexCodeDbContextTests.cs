using Microsoft.EntityFrameworkCore;
using NexCode.Data.Entities;
using NexCode.Data.Storage;

namespace NexCode.Data.Tests;

public sealed class NexCodeDbContextTests
{
    [Fact]
    public void Model_ContainsCoreFoundationTables()
    {
        var options = new DbContextOptionsBuilder<NexCodeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new NexCodeDbContext(options);

        Assert.NotNull(context.Model.FindEntityType(typeof(UserEntity)));
        Assert.NotNull(context.Model.FindEntityType(typeof(SessionEntity)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ImplementationPlanEntity)));
        Assert.NotNull(context.Model.FindEntityType(typeof(IapCacheEntity)));
    }
}
