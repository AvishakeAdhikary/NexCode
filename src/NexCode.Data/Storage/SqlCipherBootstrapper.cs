using System.Runtime.CompilerServices;

namespace NexCode.Data.Storage;

public static class SqlCipherBootstrapper
{
    private static bool _initialized;
    private static readonly object Sync = new();

    /// <summary>
    /// Runs automatically at assembly load thanks to <see cref="ModuleInitializerAttribute"/>.
    /// Ensures the SQLCipher native provider is registered with SQLitePCL before any
    /// <see cref="Microsoft.Data.Sqlite.SqliteConnection"/> static constructor runs.
    /// Because we use <c>Microsoft.Data.Sqlite.Core</c> (not the full bundle), no provider
    /// is registered by default — failing to init here causes
    /// <c>SQLitePCL.Batteries_V2.Init()</c> from <c>SqliteConnection..cctor()</c> to throw.
    /// </summary>
#pragma warning disable CA2255 // ModuleInitializer is intentional here so SQLCipher provider
    // registration races SqliteConnection's static constructor.
    [ModuleInitializer]
    internal static void ModuleInitialize() => EnsureInitialized();
#pragma warning restore CA2255

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            // Resolves to SQLitePCL.Batteries_V2 from SQLitePCLRaw.bundle_sqlcipher, which
            // calls raw.SetProvider(new SQLite3Provider_sqlcipher()). With Microsoft.Data.Sqlite.Core
            // there is no competing provider, so SQLCipher wins.
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }
}
