using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Mcp.Auth;

/// <summary>
/// Spec §17 (MCP 2025-06-18 auth): minimal PKCE OAuth helper for MCP HTTP transports.
/// Builds the authorization URL, exchanges the authorization code + verifier for an
/// access token, and produces the <c>Authorization: Bearer</c> header used on subsequent
/// MCP requests. Dropping in a real consent UI is the next step; this skeleton exists so
/// callers don't have to invent the wire shape twice.
/// </summary>
public sealed class McpOauthPkceClient
{
    public const string HttpClientName = "mcp-oauth";

    private readonly IHttpClientFactory _httpClientFactory;

    public McpOauthPkceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public McpOauthPkceChallenge CreateChallenge()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(verifierBytes);
        using var sha = SHA256.Create();
        var challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        return new McpOauthPkceChallenge(verifier, challenge);
    }

    public Uri BuildAuthorizationUrl(
        string authorizationEndpoint,
        string clientId,
        string redirectUri,
        IReadOnlyList<string> scopes,
        string codeChallenge,
        string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', scopes ?? Array.Empty<string>()),
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        var sb = new StringBuilder(authorizationEndpoint);
        sb.Append(authorizationEndpoint.Contains('?') ? '&' : '?');
        var first = true;
        foreach (var kvp in query)
        {
            if (!first)
            {
                sb.Append('&');
            }
            sb.Append(Uri.EscapeDataString(kvp.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
            first = false;
        }
        return new Uri(sb.ToString());
    }

    public async Task<McpOauthTokenResponse> ExchangeCodeAsync(
        string tokenEndpoint,
        string clientId,
        string redirectUri,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint) { Content = form };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OAuth token exchange failed ({(int)response.StatusCode}): {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new McpOauthTokenResponse(
            AccessToken: root.GetProperty("access_token").GetString() ?? string.Empty,
            TokenType: root.TryGetProperty("token_type", out var t) ? t.GetString() ?? "Bearer" : "Bearer",
            ExpiresInSeconds: root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32()
                : (int?)null,
            RefreshToken: root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null);
    }

    public AuthenticationHeaderValue ToAuthorizationHeader(McpOauthTokenResponse token)
    {
        return new AuthenticationHeaderValue(
            string.IsNullOrEmpty(token.TokenType) ? "Bearer" : token.TokenType,
            token.AccessToken);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed record McpOauthPkceChallenge(string Verifier, string CodeChallenge);

public sealed record McpOauthTokenResponse(
    string AccessToken,
    string TokenType,
    int? ExpiresInSeconds,
    string? RefreshToken);
