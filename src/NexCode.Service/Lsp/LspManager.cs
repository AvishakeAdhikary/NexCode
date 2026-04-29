using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Lsp;

/// <summary>
/// Multiplexes one LSP server per language. Slice 0015 ships TypeScript only — the
/// bridge attempts <c>npx typescript-language-server --stdio</c> and degrades gracefully
/// when npx is missing.
/// </summary>
public sealed class LspManager(ILogger<LspManager> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, LspServerHost> _servers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task<LspHoverResponse> HoverAsync(LspHoverRequest request, CancellationToken ct)
    {
        var lang = string.IsNullOrWhiteSpace(request.Language) ? GuessLanguage(request.FilePath) : request.Language!.ToLowerInvariant();
        if (!IsSupported(lang))
        {
            return new LspHoverResponse(request.FilePath, null, request.Line, request.Column, false, "lsp_unavailable");
        }

        var server = await GetOrStartAsync(lang, request.ProjectPath, ct);
        if (server is null)
        {
            return new LspHoverResponse(request.FilePath, null, request.Line, request.Column, false, "lsp_unavailable");
        }

        try
        {
            var parameters = JsonSerializer.SerializeToElement(new
            {
                textDocument = new { uri = ToUri(request.FilePath) },
                position = new { line = Math.Max(0, request.Line - 1), character = Math.Max(0, request.Column - 1) }
            }, JsonSerialization.Options);

            var result = await server.SendRequestAsync("textDocument/hover", parameters, ct);
            string? contents = null;
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("contents", out var contentsProp))
            {
                contents = contentsProp.ToString();
            }

            return new LspHoverResponse(request.FilePath, contents, request.Line, request.Column, true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "lsp hover failed for {Path}", request.FilePath);
            return new LspHoverResponse(request.FilePath, null, request.Line, request.Column, false, ex.Message);
        }
    }

    public async Task<LspDiagnosticsResponse> DiagnosticsAsync(LspDiagnosticsRequest request, CancellationToken ct)
    {
        var lang = string.IsNullOrWhiteSpace(request.Language) ? GuessLanguage(request.FilePath) : request.Language!.ToLowerInvariant();
        if (!IsSupported(lang))
        {
            return new LspDiagnosticsResponse(request.FilePath, Array.Empty<LinterMarker>(), false, "lsp_unavailable");
        }

        var server = await GetOrStartAsync(lang, request.ProjectPath, ct);
        if (server is null)
        {
            return new LspDiagnosticsResponse(request.FilePath, Array.Empty<LinterMarker>(), false, "lsp_unavailable");
        }

        // Most servers push diagnostics asynchronously via publishDiagnostics. For Slice 0015
        // we surface no markers and signal availability — full streaming wires up under
        // ServiceEventTypes.LinterDiagnostic in a follow-up slice.
        return new LspDiagnosticsResponse(request.FilePath, Array.Empty<LinterMarker>(), true, null);
    }

    private async Task<LspServerHost?> GetOrStartAsync(string language, string projectRoot, CancellationToken ct)
    {
        if (_servers.TryGetValue(language, out var existing) && existing.IsRunning)
        {
            return existing;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_servers.TryGetValue(language, out existing) && existing.IsRunning)
            {
                return existing;
            }

            var host = TryStart(language, projectRoot, ct);
            if (host is null)
            {
                return null;
            }

            _servers[language] = host;
            return host;
        }
        finally
        {
            _gate.Release();
        }
    }

    private LspServerHost? TryStart(string language, string projectRoot, CancellationToken ct)
    {
        try
        {
            var (executable, args) = ResolveCommand(language);
            if (string.IsNullOrEmpty(executable))
            {
                return null;
            }

            var host = new LspServerHost();
            host.StartAsync(executable, args, projectRoot, ct).GetAwaiter().GetResult();

            // Send minimal initialize handshake so the server can serve hover.
            var initParams = JsonSerializer.SerializeToElement(new
            {
                processId = Environment.ProcessId,
                rootUri = ToUri(projectRoot),
                capabilities = new { },
                clientInfo = new { name = "nexcode", version = "0.1.0" }
            }, JsonSerialization.Options);

            try
            {
                _ = host.SendRequestAsync("initialize", initParams, ct).GetAwaiter().GetResult();
                _ = host.SendNotificationAsync("initialized", JsonDocument.Parse("{}").RootElement);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LSP initialize failed for {Lang}", language);
                _ = host.DisposeAsync();
                return null;
            }

            return host;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "LSP server unavailable for language {Lang}.", language);
            return null;
        }
    }

    private static (string Executable, string[] Args) ResolveCommand(string language)
    {
        return language switch
        {
            "typescript" or "javascript" or "ts" or "js" => ResolveTypeScript(),
            _ => (string.Empty, Array.Empty<string>())
        };
    }

    private static (string Executable, string[] Args) ResolveTypeScript()
    {
        var npx = FindOnPath("npx.cmd") ?? FindOnPath("npx.exe") ?? FindOnPath("npx");
        if (npx is null)
        {
            return (string.Empty, Array.Empty<string>());
        }

        return (npx, new[] { "--yes", "typescript-language-server", "--stdio" });
    }

    private static bool IsSupported(string language)
    {
        return language is "typescript" or "javascript" or "ts" or "js";
    }

    private static string GuessLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
            _ => "plaintext"
        };
    }

    private static string ToUri(string path)
    {
        if (string.IsNullOrEmpty(path)) return "file:///";
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var directory in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            string candidate;
            try { candidate = Path.Combine(directory.Trim(), fileName); }
            catch (ArgumentException) { continue; }
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var host in _servers.Values)
        {
            await host.DisposeAsync();
        }
        _servers.Clear();
    }
}
