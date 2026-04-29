using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NexCode.Service.Mcp;

/// <summary>
/// MCP transport that launches a subprocess and exchanges JSON-RPC messages over its
/// stdio pipes using LSP-style <c>Content-Length</c> framed messages. Configuration shape:
/// <code>{ "command": "npx", "args": [...], "env": { ... }, "cwd": "..." }</code>
/// </summary>
public sealed class StdioMcpTransport : IMcpTransport
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Lock _writeLock = new();
    private readonly CancellationTokenSource _readerCts = new();
    private Process? _process;
    private Task? _readerTask;
    private long _idSeed;

    public event EventHandler<JsonElement>? NotificationReceived;

    public Task ConnectAsync(JsonElement config, CancellationToken cancellationToken)
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("Transport already connected.");
        }

        var command = TryGetString(config, "command")
            ?? throw new ArgumentException("stdio MCP config requires 'command'.");
        var args = new List<string>();
        if (config.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in argsEl.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    args.Add(element.GetString() ?? string.Empty);
                }
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (config.TryGetProperty("env", out var envEl) && envEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in envEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    psi.Environment[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }

        if (config.TryGetProperty("cwd", out var cwdEl) && cwdEl.ValueKind == JsonValueKind.String)
        {
            var cwd = cwdEl.GetString();
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                psi.WorkingDirectory = cwd;
            }
        }

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start MCP subprocess '{command}'.");

        _readerTask = Task.Run(() => ReaderLoopAsync(_readerCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        EnsureRunning();

        var id = Interlocked.Increment(ref _idSeed).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        await WriteFrameAsync(BuildRequest(id, method, parameters)).ConfigureAwait(false);

        using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }
    }

    public async Task SendNotificationAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        EnsureRunning();
        await WriteFrameAsync(BuildNotification(method, parameters)).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _readerCts.Cancel();
        }
        catch
        {
            // ignore — already cancelled
        }

        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best-effort
            }
        }

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask.ConfigureAwait(false);
            }
            catch
            {
                // swallow
            }
        }

        process?.Dispose();
        _process = null;

        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _readerCts.Dispose();
    }

    private void EnsureRunning()
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("MCP stdio subprocess is not running.");
        }
    }

    private async Task WriteFrameAsync(string payload)
    {
        var process = _process ?? throw new InvalidOperationException("Transport not connected.");
        var bytes = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {bytes.Length}\r\n\r\n");

        // Serialize writes so concurrent senders don't interleave frames.
        Task task;
        lock (_writeLock)
        {
            task = WriteAsync(process, header, bytes);
        }
        await task.ConfigureAwait(false);
    }

    private static async Task WriteAsync(Process process, byte[] header, byte[] body)
    {
        var stream = process.StandardInput.BaseStream;
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        var stream = process.StandardOutput.BaseStream;
        var reader = new McpFrameReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? frame;
            try
            {
                frame = await reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }

            if (frame is null)
            {
                break;
            }

            DispatchFrame(frame);
        }
    }

    private void DispatchFrame(string frame)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(frame);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
            {
                var id = idEl.ValueKind switch
                {
                    JsonValueKind.String => idEl.GetString() ?? string.Empty,
                    JsonValueKind.Number => idEl.GetRawText(),
                    _ => idEl.GetRawText()
                };
                if (_pending.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var err))
                    {
                        var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
                        tcs.TrySetException(new InvalidOperationException(msg ?? "MCP error"));
                    }
                    else if (root.TryGetProperty("result", out var resultEl))
                    {
                        tcs.TrySetResult(resultEl.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
            }
            else
            {
                NotificationReceived?.Invoke(this, root.Clone());
            }
        }
        catch (JsonException)
        {
            // ignore malformed frame
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static string BuildRequest(string id, string method, JsonElement parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                parameters.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildNotification(string method, JsonElement parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                parameters.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Helper for tests that need an LSP-framed wire payload.</summary>
    public static byte[] FrameForTesting(string payload) => McpFrameReader.FrameForTesting(payload);
}
