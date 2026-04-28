using Microsoft.Data.Sqlite;
using NexCode.Data.Storage;

namespace NexCode.Data.Tests;

public sealed class EncryptedConnectionFactoryTests
{
    [Fact]
    public void RoundTrip_CorrectKey_DecryptsExistingDatabase()
    {
        var path = NewTempDatabasePath();
        try
        {
            var key = MakeKey(0xA5);
            var factory = new EncryptedConnectionFactory(new FixedKeyDatabaseKeyProvider(key), path);

            using (var connection = factory.CreateConnection())
            {
                connection.Open();
                Execute(connection, "CREATE TABLE Secret (value TEXT NOT NULL);");
                Execute(connection, "INSERT INTO Secret (value) VALUES ('classified');");
            }

            SqliteConnection.ClearAllPools();

            using (var roundTrip = factory.CreateConnection())
            {
                roundTrip.Open();
                using var command = roundTrip.CreateCommand();
                command.CommandText = "SELECT value FROM Secret;";
                var value = (string?)command.ExecuteScalar();
                Assert.Equal("classified", value);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void RoundTrip_WrongKey_FailsToDecrypt()
    {
        var path = NewTempDatabasePath();
        try
        {
            var rightKey = MakeKey(0x11);
            using (var connection = new EncryptedConnectionFactory(new FixedKeyDatabaseKeyProvider(rightKey), path).CreateConnection())
            {
                connection.Open();
                Execute(connection, "CREATE TABLE Secret (value TEXT NOT NULL);");
                Execute(connection, "INSERT INTO Secret (value) VALUES ('classified');");
            }

            SqliteConnection.ClearAllPools();

            // SQLCipher will fail HMAC verification when the wrong key is keyed against an
            // already-encrypted database. With our forced sqlite_master read inside ApplyKey,
            // the failure surfaces during Open(), not later.
            var wrongKey = MakeKey(0x22);
            var wrongFactory = new EncryptedConnectionFactory(new FixedKeyDatabaseKeyProvider(wrongKey), path);

            Assert.Throws<SqliteException>(() =>
            {
                using var wrong = wrongFactory.CreateConnection();
                wrong.Open();
            });
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void EncryptedDatabase_OnDisk_IsNotPlaintext()
    {
        var path = NewTempDatabasePath();
        try
        {
            var factory = new EncryptedConnectionFactory(new FixedKeyDatabaseKeyProvider(MakeKey(0x33)), path);
            using (var connection = factory.CreateConnection())
            {
                connection.Open();
                Execute(connection, "CREATE TABLE Marker (value TEXT NOT NULL);");
                Execute(connection, "INSERT INTO Marker (value) VALUES ('uniquetestmarkerstring');");
            }

            SqliteConnection.ClearAllPools();
            // Allow the OS time to release the file handle before we read it raw.
            // Microsoft.Data.Sqlite's pool clear is synchronous but file-system close can lag.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using var probe = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
            }

            var raw = File.ReadAllBytes(path);
            var asAscii = System.Text.Encoding.ASCII.GetString(raw);
            Assert.DoesNotContain("uniquetestmarkerstring", asAscii);

            // The canonical SQLite plaintext header is "SQLite format 3\0". An encrypted
            // SQLCipher database does not start with that header.
            var headerBytes = raw.AsSpan(0, Math.Min(16, raw.Length));
            var header = System.Text.Encoding.ASCII.GetString(headerBytes);
            Assert.False(header.StartsWith("SQLite format 3", StringComparison.Ordinal),
                "Database file starts with the plaintext SQLite header — encryption did not apply.");
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void Constructor_NullKeyProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EncryptedConnectionFactory(null!, "irrelevant.db"));
    }

    [Fact]
    public void Constructor_BlankDataSource_Throws()
    {
        var key = new FixedKeyDatabaseKeyProvider(MakeKey(0x44));
        Assert.Throws<ArgumentException>(() => new EncryptedConnectionFactory(key, ""));
    }

    [Fact]
    public void FixedKeyDatabaseKeyProvider_RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => new FixedKeyDatabaseKeyProvider(new byte[16]));
        Assert.Throws<ArgumentException>(() => new FixedKeyDatabaseKeyProvider(new byte[64]));
    }

    private static byte[] MakeKey(byte fill)
    {
        var key = new byte[IDatabaseKeyProvider.KeyLengthBytes];
        Array.Fill(key, fill);
        return key;
    }

    private static string NewTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"nexcode-enc-tests-{Guid.NewGuid():N}.db");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
        {
            if (!File.Exists(file)) continue;
            try { File.Delete(file); }
            catch (IOException) { /* best-effort */ }
        }
    }
}
