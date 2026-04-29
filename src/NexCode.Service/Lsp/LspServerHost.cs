using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexCode.Shared.Json;

namespace NexCode.Service.Lsp;

/// <summary>
/// Manages a single language-server child process, framing JSON-RPC over stdio with
/// LSP <c>Content-Length</c> headers. Supports request/response correlation by id and
/// fire-and-forget notifications.
/// </summary>
public sealed class LspServerHost : IAsyncDisposable
{
    private readonly object _writeSync = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private Stream? _stdin;
    private Stream? _stdout;
    private Task? _readLoop;
    private CancellationTokenSource? _cts;
    private int _nextId;
    private bool _disposed;

    public event EventHandler<JsonElement>? NotificationReceived;

    public bool IsRunning => _process is { HasExited: false };

    public Task StartAsync(string executable, string[] args, string workingDir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false)
        };

        if (args is not null)
        {
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!_process.Start())
        {
            throw new InvalidOperationException($"Failed to start LSP server '{executable}'.");
        }

        _stdin = _process.StandardInput.BaseStream;
        _stdout = _process.StandardOutput.BaseStream;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoop = Task.Run(() => ReadLoopAsync(_stdout, _cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task<JsonElement> SendRequestAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        if (_stdin is null)
        {
            throw new InvalidOperationException("LSP server is not running.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var envelope = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        WriteFrame(JsonSerializer.SerializeToUtf8Bytes(envelope, JsonSerialization.Options));

        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            return await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task SendNotificationAsync(string method, JsonElement parameters)
    {
        if (_stdin is null)
        {
            return Task.CompletedTask;
        }

        var envelope = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };
        WriteFrame(JsonSerializer.SerializeToUtf8Bytes(envelope, JsonSerialization.Options));
        return Task.CompletedTask;
    }

    public void WriteFrame(byte[] body)
    {
        if (_stdin is null)
        {
            return;
        }

        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeSync)
        {
            _stdin.Write(header, 0, header.Length);
            _stdin.Write(body, 0, body.Length);
            _stdin.Flush();
        }
    }

    public static byte[]? ReadFrame(Stream stream)
    {
        var headers = ReadHeaders(stream);
        if (headers is null)
        {
            return null;
        }

        if (!headers.TryGetValue("content-length", out var lengthValue)
            || !int.TryParse(lengthValue, out var length)
            || length < 0)
        {
            return null;
        }

        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = stream.Read(buffer, read, length - read);
            if (n <= 0)
            {
                return null;
            }
            read += n;
        }

        return buffer;
    }

    private async Task ReadLoopAsync(Stream stream, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[]? body;
                try
                {
                    body = await Task.Run(() => ReadFrame(stream), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    return;
                }

                if (body is null)
                {
                    return;
                }

                JsonElement root;
                try
                {
                    root = JsonDocument.Parse(body).RootElement;
                }
                catch
                {
                    continue;
                }

                if (root.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.Number
                    && idProp.TryGetInt32(out var id)
                    && _pending.TryRemove(id, out var pending))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        pending.TrySetException(new InvalidOperationException(error.ToString()));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        pending.TrySetResult(result.Clone());
                    }
                    else
                    {
                        pending.TrySetResult(default);
                    }
                }
                else if (root.TryGetProperty("method", out _))
                {
                    NotificationReceived?.Invoke(this, root.Clone());
                }
            }
        }
        catch
        {
            // best-effort drain
        }
    }

    private static System.Collections.Generic.Dictionary<string, string>? ReadHeaders(Stream stream)
    {
        var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new System.Collections.Generic.List<byte>(256);

        while (true)
        {
            var read = stream.ReadByte();
            if (read < 0)
            {
                return buffer.Count == 0 ? null : null;
            }

            buffer.Add((byte)read);
            var len = buffer.Count;
            if (len >= 4
                && buffer[len - 4] == (byte)'\r'
                && buffer[len - 3] == (byte)'\n'
                && buffer[len - 2] == (byte)'\r'
                && buffer[len - 1] == (byte)'\n')
            {
                break;
            }

            if (len > 16384)
            {
                return null;
            }
        }

        var raw = Encoding.ASCII.GetString(buffer.ToArray(), 0, buffer.Count - 4);
        var lines = raw.Split("\r\n");
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            headers[key] = value;
        }

        return headers;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }

        if (_readLoop is not null)
        {
            await Task.WhenAny(_readLoop, Task.Delay(250));
        }

        _stdin?.Dispose();
        _stdout?.Dispose();
        _process?.Dispose();
    }
}
