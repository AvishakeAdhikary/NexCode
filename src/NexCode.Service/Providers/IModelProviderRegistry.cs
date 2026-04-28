namespace NexCode.Service.Providers;

/// <summary>
/// Resolves the active <see cref="IModelProvider"/> for a session based on either an
/// explicit override or the user's configured default provider.
/// </summary>
public interface IModelProviderRegistry
{
    /// <summary>All registered providers (Anthropic, OpenAI, …).</summary>
    IReadOnlyList<IModelProvider> All { get; }

    /// <summary>Look up a provider by its <see cref="IModelProvider.Key"/>.</summary>
    IModelProvider? FindByKey(string key);
}
