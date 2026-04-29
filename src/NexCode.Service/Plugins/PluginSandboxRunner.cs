using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Json;

namespace NexCode.Service.Plugins;

/// <summary>
/// Spawns a plugin entry-point as a child process under a Windows Job Object so that the
/// runtime can hard-kill the entire process tree on uninstall, helper shutdown, or hook
/// timeout. Communicates over stdio via line-delimited JSON-RPC requests with a single
/// per-call response.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PluginSandboxRunner(ILogger<PluginSandboxRunner> logger) : IAsyncDisposable
{
    private readonly Dictionary<Guid, PluginProcessHandle> _running = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<JsonElement?> InvokeHookAsync(
        Guid pluginId,
        string installPath,
        PluginManifest manifest,
        string hook,
        object payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var handle = await EnsureRunningAsync(pluginId, installPath, manifest, cancellationToken).ConfigureAwait(false);
        if (handle is null)
        {
            return null;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = hook,
            @params = payload
        };

        using var perCallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perCallCts.CancelAfter(timeout);

        try
        {
            await handle.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonSerialization.Options),
                perCallCts.Token).ConfigureAwait(false);
            var responseLine = await handle.ReadLineAsync(perCallCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(responseLine);
            return doc.RootElement.TryGetProperty("result", out var result)
                ? result.Clone()
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Plugin {PluginId} hook {Hook} timed out; killing process.", pluginId, hook);
            await StopAsync(pluginId).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {PluginId} hook {Hook} threw.", pluginId, hook);
            await StopAsync(pluginId).ConfigureAwait(false);
            return null;
        }
    }

    public async Task StopAsync(Guid pluginId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_running.Remove(pluginId, out var handle))
            {
                handle.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var handle in _running.Values)
            {
                handle.Dispose();
            }
            _running.Clear();
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }

    private async Task<PluginProcessHandle?> EnsureRunningAsync(
        Guid pluginId,
        string installPath,
        PluginManifest manifest,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_running.TryGetValue(pluginId, out var existing) && existing.IsAlive)
            {
                return existing;
            }

            if (existing is not null)
            {
                existing.Dispose();
                _running.Remove(pluginId);
            }

            var entryPoint = Path.Combine(installPath, manifest.EntryPoint);
            if (!File.Exists(entryPoint))
            {
                logger.LogWarning(
                    "Plugin {PluginId} entry point {EntryPoint} does not exist.",
                    pluginId,
                    entryPoint);
                return null;
            }

            var (exe, args) = ResolveExecutable(entryPoint);
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = installPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logger.LogDebug("[plugin {PluginId}] {Line}", pluginId, e.Data);
                }
            };

            if (!process.Start())
            {
                logger.LogWarning("Plugin {PluginId} failed to start.", pluginId);
                return null;
            }

            process.BeginErrorReadLine();

            var jobHandle = MinimalJobObject.CreateAndAssign(process);
            var handle = new PluginProcessHandle(process, jobHandle);
            _running[pluginId] = handle;
            return handle;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static (string Executable, string Arguments) ResolveExecutable(string entryPoint)
    {
        var ext = Path.GetExtension(entryPoint).ToLowerInvariant();
        return ext switch
        {
            ".exe" => (entryPoint, string.Empty),
            ".dll" => ("dotnet", $"\"{entryPoint}\""),
            ".js" => ("node", $"\"{entryPoint}\""),
            ".py" => ("python", $"\"{entryPoint}\""),
            _ => (entryPoint, string.Empty)
        };
    }

    private sealed class PluginProcessHandle(Process process, IntPtr jobHandle) : IDisposable
    {
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        public bool IsAlive => !process.HasExited;

        public async Task WriteLineAsync(string payload, CancellationToken cancellationToken)
        {
            await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        public void Dispose()
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
                // Best-effort.
            }

            if (jobHandle != IntPtr.Zero)
            {
                MinimalJobObject.Close(jobHandle);
            }

            process.Dispose();
            _ioGate.Dispose();
        }
    }

    private static class MinimalJobObject
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob,
            int infoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        public static IntPtr CreateAndAssign(Process process)
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            var size = Marshal.SizeOf(info);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
                SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size);
                AssignProcessToJobObject(job, process.Handle);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return job;
        }

        public static void Close(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
