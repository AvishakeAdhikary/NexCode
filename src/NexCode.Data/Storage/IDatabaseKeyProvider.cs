namespace NexCode.Data.Storage;

/// <summary>
/// Supplies the 32-byte SQLCipher key derived per spec §2.3 + Appendix A.
/// Implementations must produce the same key across calls for the lifetime of the install,
/// and must never persist the derived key to disk.
/// </summary>
public interface IDatabaseKeyProvider
{
    /// <summary>The canonical SQLCipher key length in bytes (256 bits).</summary>
    public const int KeyLengthBytes = 32;

    /// <summary>
    /// Returns the 32-byte SQLCipher key. Implementations may cache; callers must not mutate
    /// the returned array. The array is the canonical key used to issue
    /// <c>PRAGMA key = "x'&lt;hex&gt;'";</c> on every newly opened connection.
    /// </summary>
    byte[] GetKey();
}
