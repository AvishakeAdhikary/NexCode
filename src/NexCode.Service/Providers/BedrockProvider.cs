using System.Runtime.CompilerServices;

namespace NexCode.Service.Providers;

/// <summary>
/// AWS Bedrock <c>InvokeModelWithResponseStream</c> skeleton. SigV4 signing + binary
/// event stream framing is intentionally not yet wired; this adapter exists so the
/// helper can register the provider and report a clean configuration error rather
/// than crashing when Bedrock is selected without credentials.
/// <para>
/// Configuration: <see cref="ProviderConfiguration.ApiKey"/> must be a pipe-delimited
/// triple <c>accessKeyId|secretAccessKey|region</c>. The first SigV4-capable build
/// turn this into a real adapter.
/// </para>
/// </summary>
public sealed class BedrockProvider : IModelProvider
{
    public const string HttpClientName = "bedrock";

    private readonly IHttpClientFactory _httpClientFactory;

    public BedrockProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string Key => "bedrock";
    public string DisplayName => "AWS Bedrock";

    public async IAsyncEnumerable<ProviderEvent> StreamTurnAsync(
        ProviderTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Task.CompletedTask.ConfigureAwait(false);

        var apiKey = request.Configuration.ApiKey ?? string.Empty;
        var parts = apiKey.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
        {
            yield return new ProviderErrorEvent(
                "bedrock.not_configured",
                "AWS Bedrock requires an ApiKey of the form 'accessKeyId|secretAccessKey|region'.",
                Recoverable: false);
            yield break;
        }

        // SigV4 binary event-stream support is the next iteration. Until then, surface a
        // clean error event so the agent loop can fall back to another provider.
        yield return new ProviderErrorEvent(
            "bedrock.not_implemented",
            "Bedrock streaming is not implemented in this build. Use another provider for now.",
            Recoverable: false);

        // Reference _httpClientFactory so the field isn't flagged as unused; the SigV4
        // implementation will use it to issue requests.
        _ = _httpClientFactory;
    }
}
