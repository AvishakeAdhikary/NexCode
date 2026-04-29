using System.Net.Http.Headers;
using System.Text.Json;

namespace NexCode.Marketplace.Sdk;

/// <summary>
/// Spec §18.3 — HTTP client for uploading a packaged plugin/MCP-server artifact
/// to the configured marketplace endpoint. Endpoint is read from the
/// <c>NEXCODE_MARKETPLACE_ENDPOINT</c> environment variable; if it's unset the
/// client returns a friendly error rather than throwing.
/// </summary>
public sealed class PublishClient
{
    public const string EndpointEnvironmentVariable = "NEXCODE_MARKETPLACE_ENDPOINT";

    private readonly HttpClient _http;
    private readonly Func<string?> _endpointResolver;

    public PublishClient(HttpClient? http = null, Func<string?>? endpointResolver = null)
    {
        _http = http ?? new HttpClient();
        _endpointResolver = endpointResolver
            ?? (() => Environment.GetEnvironmentVariable(EndpointEnvironmentVariable));
    }

    /// <summary>
    /// Upload <paramref name="artifact"/> as <c>application/zip</c> to
    /// <c>{endpoint}/api/publish</c>. Returns the listing id on success or a
    /// <see cref="PublishResult"/> with <see cref="PublishResult.Success"/>=false otherwise.
    /// </summary>
    public async Task<PublishResult> PublishAsync(
        byte[] artifact,
        string artifactName,
        string artifactKind,
        string? authToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Length == 0)
        {
            return new PublishResult(false, null, "artifact is empty");
        }

        var endpoint = _endpointResolver();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new PublishResult(
                Success: false,
                ListingId: null,
                Message: $"marketplace endpoint not configured. Set {EndpointEnvironmentVariable}.");
        }

        using var content = new MultipartFormDataContent();
        var artifactContent = new ByteArrayContent(artifact);
        artifactContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(artifactContent, "artifact", artifactName);
        content.Add(new StringContent(artifactKind), "kind");

        using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(endpoint, "/api/publish"))
        {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PublishResult(false, null, $"HTTP {(int)response.StatusCode}: {body}");
            }

            string? id = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    id = idElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Body is not JSON; fall through with null id.
            }

            return new PublishResult(true, id, body);
        }
        catch (HttpRequestException ex)
        {
            return new PublishResult(false, null, $"network error: {ex.Message}");
        }
    }

    private static string CombineUrl(string endpoint, string path)
    {
        if (endpoint.EndsWith('/')) endpoint = endpoint[..^1];
        if (!path.StartsWith('/')) path = "/" + path;
        return endpoint + path;
    }
}

public sealed record PublishResult(bool Success, string? ListingId, string Message);
