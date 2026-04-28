using Microsoft.Data.Sqlite;

namespace NexCode.Data.Storage;

/// <summary>
/// The only supported way to obtain a <see cref="SqliteConnection"/> against the NexCode
/// database. Hooks <see cref="SqliteConnection.StateChange"/> so that the first
/// <see cref="System.Data.ConnectionState.Open"/> transition issues
/// <c>PRAGMA key = "x'&lt;hex&gt;'";</c> before the caller can run any SQL.
/// </summary>
/// <remarks>
/// EF Core consumers should use <see cref="SqlCipherKeyInterceptor"/> instead — that path
/// participates in EF Core's interceptor chain. This factory exists for raw-connection
/// scenarios (migrations, ad-hoc queries, tests).
/// </remarks>
public sealed class EncryptedConnectionFactory
{
    private readonly IDatabaseKeyProvider _keyProvider;
    private readonly string _dataSource;

    public EncryptedConnectionFactory(IDatabaseKeyProvider keyProvider, string dataSource)
    {
        SqlCipherBootstrapper.EnsureInitialized();

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new ArgumentException("Data source path must be provided.", nameof(dataSource));
        }

        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _dataSource = dataSource;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string ConnectionString { get; }

    public string DataSource => _dataSource;

    /// <summary>
    /// Returns a connection that will issue <c>PRAGMA key</c> on the first transition to
    /// <see cref="System.Data.ConnectionState.Open"/>. Caller owns the connection lifetime.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        AttachKeyOnOpen(connection, _keyProvider);
        return connection;
    }

    /// <summary>
    /// Public hook used by <see cref="EncryptedConnectionDbContextOptionsExtensions"/> when
    /// EF Core opens its own connection from a connection string. Idempotent — guards against
    /// double-attach on the same connection instance.
    /// </summary>
    internal static void AttachKeyOnOpen(SqliteConnection connection, IDatabaseKeyProvider keyProvider)
    {
        var keyed = false;

        connection.StateChange += (_, args) =>
        {
            if (keyed || args.CurrentState != System.Data.ConnectionState.Open)
            {
                return;
            }

            SqlCipherKeyInterceptor.ApplyKey(connection, keyProvider);
            keyed = true;
        };
    }
}
