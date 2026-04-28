using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NexCode.Data.Storage;

/// <summary>
/// EF Core interceptor that issues <c>PRAGMA key</c> against the canonical 32-byte SQLCipher
/// key on every newly opened connection. Without this interceptor, the SQLCipher binding
/// loads but the database is opened plaintext — the failure mode that broke spec §2.3 prior
/// to Slice 0011.
/// </summary>
public sealed class SqlCipherKeyInterceptor(IDatabaseKeyProvider keyProvider) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyKey(connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ApplyKey(connection);
        return Task.CompletedTask;
    }

    internal static void ApplyKey(DbConnection connection, IDatabaseKeyProvider keyProvider)
    {
        var key = keyProvider.GetKey();
        var hex = Convert.ToHexString(key);

        // cipher_compatibility = 4 selects the SQLCipher 4 default page size / KDF iteration count.
        // It must be issued before PRAGMA key — once the key has been processed, the cipher
        // settings for that connection are locked in.
        using (var compat = connection.CreateCommand())
        {
            compat.CommandText = "PRAGMA cipher_compatibility = 4;";
            compat.ExecuteNonQuery();
        }

        // PRAGMA key with the "x'<hex>'" form passes the 32-byte key as the raw cipher key
        // (no internal PBKDF2 by SQLCipher). The hex form is the spec-required path.
        using (var rekey = connection.CreateCommand())
        {
            rekey.CommandText = $"PRAGMA key = \"x'{hex}'\";";
            rekey.ExecuteNonQuery();
        }

        // Force a page read so an incorrect key fails fast at Open time, rather than later when
        // the caller runs their first SELECT. cipher_version is a no-op on a healthy connection
        // and triggers SQLCipher's HMAC verification against the first page when the database
        // file already exists.
        using (var verify = connection.CreateCommand())
        {
            verify.CommandText = "SELECT count(*) FROM sqlite_master;";
            verify.ExecuteScalar();
        }
    }

    private void ApplyKey(DbConnection connection) => ApplyKey(connection, keyProvider);
}
