using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexCode.Data.Repositories;
using NexCode.Data.Storage;

namespace NexCode.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNexCodeData(this IServiceCollection services, string connectionString)
    {
        SqlCipherBootstrapper.EnsureInitialized();
        services.AddDbContextFactory<NexCodeDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<NexCodeDatabaseInitializer>();
        services.AddSingleton<IAccountRepository, AccountRepository>();
        return services;
    }
}
