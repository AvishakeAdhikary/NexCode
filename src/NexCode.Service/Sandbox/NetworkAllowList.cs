namespace NexCode.Service.Sandbox;

/// <summary>
/// Spec §14.1 partial: when a sandboxed session executes a shell command, refuse argv
/// strings that reference hostnames outside an allowlist. The list contains the
/// LLM provider hostnames plus any user-configured MCP endpoints.
/// <para>
/// This is a best-effort string match — full OS-level network blocking via Windows
/// Filtering Platform is deferred to Slice 0019. The sandbox already disables
/// <c>execute_command</c> wholesale; this gate is the secondary defence-in-depth check
/// applied before the guard short-circuits.
/// </para>
/// </summary>
public static class NetworkAllowList
{
    /// <summary>Always-allowed provider hostnames (spec §6.1 + §13.1).</summary>
    public static readonly IReadOnlySet<string> ProviderHostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.anthropic.com",
        "api.openai.com",
        "api.openrouter.ai",
        "openrouter.ai",
        "generativelanguage.googleapis.com",
        "api.x.ai",
        "api.deepseek.com",
        "api.mistral.ai",
        "api.groq.com",
        "api.cohere.ai",
        "api.together.xyz",
        "api.perplexity.ai"
    };

    /// <summary>Always-allowed local loopback hosts (no exfiltration risk).</summary>
    public static readonly IReadOnlySet<string> LoopbackHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "127.0.0.1",
        "::1",
        "0.0.0.0"
    };

    /// <summary>
    /// Returns <c>true</c> when no host pattern in <paramref name="argv"/> falls outside the
    /// configured allowlist. Returns <c>false</c> with the offending host in
    /// <paramref name="violatingHost"/> when a match is found.
    /// </summary>
    public static bool IsCommandAllowed(
        string argv,
        IEnumerable<string> mcpEndpointHosts,
        out string? violatingHost)
    {
        violatingHost = null;
        if (string.IsNullOrWhiteSpace(argv)) return true;

        var allow = new HashSet<string>(ProviderHostnames, StringComparer.OrdinalIgnoreCase);
        foreach (var host in LoopbackHosts) allow.Add(host);
        foreach (var host in mcpEndpointHosts)
        {
            if (string.IsNullOrWhiteSpace(host)) continue;
            allow.Add(NormalizeHost(host));
        }

        foreach (var token in Tokenize(argv))
        {
            if (!LooksLikeHost(token, out var candidate)) continue;
            if (allow.Contains(candidate)) continue;
            // Wildcard match — allow if the token ends with an entry like ".anthropic.com"
            // when the allowlist holds "api.anthropic.com". Conservative: require exact.
            violatingHost = candidate;
            return false;
        }

        return true;
    }

    /// <summary>Normalize a URL or bare host string to its lowercased hostname.</summary>
    public static string NormalizeHost(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim().Trim('"', '\'');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Host.ToLowerInvariant();
        }
        var slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex > 0) trimmed = trimmed[..slashIndex];
        var colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex > 0) trimmed = trimmed[..colonIndex];
        return trimmed.ToLowerInvariant();
    }

    private static IEnumerable<string> Tokenize(string argv)
    {
        var separators = new[] { ' ', '\t', '\n', '\r', '|', '&', ';', '`' };
        return argv.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool LooksLikeHost(string token, out string host)
    {
        host = NormalizeHost(token);
        if (host.Length == 0) return false;
        if (LoopbackHosts.Contains(host)) return true;
        // Heuristic: at least one dot and only host-allowed characters.
        if (!host.Contains('.', StringComparison.Ordinal)) return false;
        foreach (var c in host)
        {
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-')) return false;
        }
        return true;
    }
}
