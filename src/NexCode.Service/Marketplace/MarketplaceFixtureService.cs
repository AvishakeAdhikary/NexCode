using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NexCode.Service.Marketplace;

/// <summary>
/// Spec §18.2 demo marketplace client. Returns a curated catalog of MCP servers / plugins
/// from the bundled <c>Fixtures/catalog.json</c> when running offline, and switches to
/// a real HTTP backend when the <c>NEXCODE_MARKETPLACE_ENDPOINT</c> environment variable
/// is set (issuing <c>GET {endpoint}/v1/catalog</c>).
/// </summary>
public sealed class MarketplaceFixtureService
{
    public const string HttpClientName = "marketplace";
    public const string EndpointEnvVar = "NEXCODE_MARKETPLACE_ENDPOINT";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MarketplaceFixtureService> _logger;
    private JsonElement? _cached;
    private readonly Lock _cacheLock = new();

    public MarketplaceFixtureService(
        IHttpClientFactory httpClientFactory,
        ILogger<MarketplaceFixtureService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Resolves the catalog payload. Returns offline fixtures unless the env var is set.</summary>
    public async Task<JsonElement> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            try
            {
                return await FetchOnlineAsync(endpoint.TrimEnd('/'), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Marketplace fetch from {Endpoint} failed; falling back to fixtures.", endpoint);
            }
        }

        return LoadFixture();
    }

    private async Task<JsonElement> FetchOnlineAsync(string endpoint, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client
            .GetAsync($"{endpoint}/v1/catalog", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private JsonElement LoadFixture()
    {
        lock (_cacheLock)
        {
            if (_cached is { } cached)
            {
                return cached;
            }

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Marketplace", "Fixtures", "catalog.json");
            if (!File.Exists(fixturePath))
            {
                // Try the source-tree path for development scenarios.
                fixturePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..",
                    "Marketplace",
                    "Fixtures",
                    "catalog.json");
            }

            if (!File.Exists(fixturePath))
            {
                using var emptyDoc = JsonDocument.Parse("{\"entries\":[]}");
                _cached = emptyDoc.RootElement.Clone();
                return _cached.Value;
            }

            var raw = File.ReadAllText(fixturePath);
            using var doc = JsonDocument.Parse(raw);
            _cached = doc.RootElement.Clone();
            return _cached.Value;
        }
    }
}
