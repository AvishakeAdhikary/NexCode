namespace NexCode.Data.Storage;

public static class SqlCipherBootstrapper
{
    private static bool _initialized;
    private static readonly object Sync = new();

    public static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }
}
