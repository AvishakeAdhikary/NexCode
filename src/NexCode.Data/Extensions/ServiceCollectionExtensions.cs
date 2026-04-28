using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexCode.Data.Repositories;
using NexCode.Data.Storage;

namespace NexCode.Data.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the encrypted NexCode data layer. Callers must register an
    /// <see cref="IDatabaseKeyProvider"/> in the same container before the first
    /// <see cref="NexCodeDbContext"/> resolution, otherwise resolution will fail.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="dataSource">
    /// SQLite database file path. Use the bare path (e.g. <c>%LocalAppData%\NexCode\Data\nexcode.db</c>),
    /// not a connection string. The factory will build the connection string and own keying.
    /// </param>
    public static IServiceCollection AddNexCodeData(this IServiceCollection services, string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new ArgumentException("Data source path must be provided.", nameof(dataSource));
        }

        SqlCipherBootstrapper.EnsureInitialized();

        services.AddSingleton(sp =>
        {
            var keyProvider = sp.GetRequiredService<IDatabaseKeyProvider>();
            return new EncryptedConnectionFactory(keyProvider, dataSource);
        });

        services.AddSingleton(sp => new SqlCipherKeyInterceptor(sp.GetRequiredService<IDatabaseKeyProvider>()));

        services.AddDbContextFactory<NexCodeDbContext>((sp, options) =>
        {
            var factory = sp.GetRequiredService<EncryptedConnectionFactory>();
            var interceptor = sp.GetRequiredService<SqlCipherKeyInterceptor>();
            options.UseSqlite(factory.ConnectionString);
            options.AddInterceptors(interceptor);
        });

        services.AddSingleton<NexCodeDatabaseInitializer>();
        services.AddSingleton<IAccountRepository, AccountRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        return services;
    }
}
