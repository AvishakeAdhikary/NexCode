namespace NexCode.Data.Storage;

/// <summary>
/// Test / design-time key provider that returns a fixed 32-byte key. Production callers
/// must use <see cref="DpapiDatabaseKeyProvider"/> instead.
/// </summary>
public sealed class FixedKeyDatabaseKeyProvider : IDatabaseKeyProvider
{
    private readonly byte[] _key;

    public FixedKeyDatabaseKeyProvider(byte[] key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length != IDatabaseKeyProvider.KeyLengthBytes)
        {
            throw new ArgumentException(
                $"Key must be exactly {IDatabaseKeyProvider.KeyLengthBytes} bytes; got {key.Length}.",
                nameof(key));
        }

        _key = (byte[])key.Clone();
    }

    public byte[] GetKey() => _key;
}
