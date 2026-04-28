namespace NexCode.Service.Providers;

/// <summary>
/// Concrete <see cref="IModelProviderRegistry"/> backed by DI. Receives every registered
/// <see cref="IModelProvider"/> and indexes them by <see cref="IModelProvider.Key"/>.
/// </summary>
public sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly IReadOnlyList<IModelProvider> _all;
    private readonly Dictionary<string, IModelProvider> _byKey;

    public ModelProviderRegistry(IEnumerable<IModelProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _all = providers.ToList();
        _byKey = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _all)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
            {
                continue;
            }
            _byKey[provider.Key] = provider;
        }
    }

    public IReadOnlyList<IModelProvider> All => _all;

    public IModelProvider? FindByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }
        return _byKey.TryGetValue(key, out var provider) ? provider : null;
    }
}
