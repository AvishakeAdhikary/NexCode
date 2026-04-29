using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace NexCode.Service.Terminal;

/// <summary>
/// Win32 ConPTY wrapper. Spawns a child shell process attached to a pseudo-console and
/// pumps bytes between the host pipes and managed callers. Spec §6 / §15.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ConPtyHost : IAsyncDisposable, IDisposable
{
    private SafeFileHandle? _inputReadSide;
    private SafeFileHandle? _inputWriteSide;
    private SafeFileHandle? _outputReadSide;
    private SafeFileHandle? _outputWriteSide;
    private IntPtr _hPC = IntPtr.Zero;
    private FileStream? _inputWriter;
    private FileStream? _outputReader;
    private CancellationTokenSource? _readCts;
    private Task? _readLoopTask;
    private System.Diagnostics.Process? _process;
    private int _disposed;

    public event EventHandler<byte[]>? OutputReceived;
    public event EventHandler<int>? Exited;

    public bool IsRunning => _process is { HasExited: false };

    public Task StartAsync(
        string shellExecutable,
        string[] args,
        string workingDir,
        string[] envVars,
        ConPtySize size,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shellExecutable))
        {
            throw new ArgumentException("Shell executable required.", nameof(shellExecutable));
        }

        ct.ThrowIfCancellationRequested();

        if (!CreatePipe(out _inputReadSide, out _inputWriteSide, IntPtr.Zero, 0)
            || !CreatePipe(out _outputReadSide, out _outputWriteSide, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException("Failed to allocate ConPTY pipes.");
        }

        var coord = new Coord((short)Math.Max(1, size.Cols), (short)Math.Max(1, size.Rows));
        var hr = CreatePseudoConsole(coord, _inputReadSide!, _outputWriteSide!, 0, out _hPC);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreatePseudoConsole failed (hr=0x{hr:X8}).");
        }

        _inputWriter = new FileStream(_inputWriteSide!, FileAccess.Write, 4096, isAsync: false);
        _outputReader = new FileStream(_outputReadSide!, FileAccess.Read, 4096, isAsync: false);

        var commandLine = BuildCommandLine(shellExecutable, args);
        StartProcess(commandLine, workingDir, envVars);

        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoopTask = Task.Run(() => PumpOutputAsync(_readCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] data)
    {
        if (data is null || data.Length == 0 || _inputWriter is null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                _inputWriter.Write(data, 0, data.Length);
                _inputWriter.Flush();
            }
            catch
            {
                // pipe closed
            }
        });
    }

    public Task ResizeAsync(int cols, int rows)
    {
        if (_hPC == IntPtr.Zero)
        {
            return Task.CompletedTask;
        }

        var coord = new Coord((short)Math.Max(1, cols), (short)Math.Max(1, rows));
        ResizePseudoConsole(_hPC, coord);
        return Task.CompletedTask;
    }

    public void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private void StartProcess(string commandLine, string workingDir, string[] envVars)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = SplitFirstToken(commandLine, out var rest),
            Arguments = rest,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            // The PTY owns the stdio handles — do not redirect them on the managed side.
        };

        if (envVars is not null)
        {
            foreach (var entry in envVars)
            {
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }

                var idx = entry.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                startInfo.EnvironmentVariables[entry[..idx]] = entry[(idx + 1)..];
            }
        }

        _process = new System.Diagnostics.Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _process.Exited += (_, _) =>
        {
            int exitCode;
            try { exitCode = _process?.ExitCode ?? -1; } catch { exitCode = -1; }
            Exited?.Invoke(this, exitCode);
        };

        _process.Start();
    }

    private async Task PumpOutputAsync(CancellationToken token)
    {
        if (_outputReader is null)
        {
            return;
        }

        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await _outputReader.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                var slice = new byte[read];
                Buffer.BlockCopy(buffer, 0, slice, 0, read);
                OutputReceived?.Invoke(this, slice);
            }
        }
        catch
        {
            // best-effort
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try { _readCts?.Cancel(); } catch { }
        try
        {
            if (_readLoopTask is not null)
            {
                await Task.WhenAny(_readLoopTask, Task.Delay(250));
            }
        }
        catch { }

        try { Kill(); } catch { }

        if (_hPC != IntPtr.Zero)
        {
            ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }

        _inputWriter?.Dispose();
        _outputReader?.Dispose();
        _inputReadSide?.Dispose();
        _inputWriteSide?.Dispose();
        _outputReadSide?.Dispose();
        _outputWriteSide?.Dispose();
        _process?.Dispose();
    }

    private static string BuildCommandLine(string executable, string[] args)
    {
        var quoted = QuoteIfNeeded(executable);
        if (args is null || args.Length == 0)
        {
            return quoted;
        }
        return $"{quoted} {string.Join(' ', Array.ConvertAll(args, QuoteIfNeeded))}";
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return value.Contains(' ') && !value.StartsWith('"') ? $"\"{value}\"" : value;
    }

    private static string SplitFirstToken(string commandLine, out string rest)
    {
        if (commandLine.StartsWith('"'))
        {
            var end = commandLine.IndexOf('"', 1);
            if (end > 0)
            {
                rest = end + 1 < commandLine.Length ? commandLine[(end + 1)..].Trim() : string.Empty;
                return commandLine.Substring(1, end - 1);
            }
        }
        var spaceIdx = commandLine.IndexOf(' ');
        if (spaceIdx < 0)
        {
            rest = string.Empty;
            return commandLine;
        }
        rest = commandLine[(spaceIdx + 1)..].Trim();
        return commandLine[..spaceIdx];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
        public Coord(short x, short y) { X = x; Y = y; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        Coord size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ClosePseudoConsole(IntPtr hPC);
}

public readonly record struct ConPtySize(int Cols, int Rows);
