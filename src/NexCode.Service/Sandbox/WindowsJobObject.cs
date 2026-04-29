using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NexCode.Service.Sandbox;

/// <summary>
/// Spec §14.1 sandbox primitive. Wraps a Win32 Job Object so that any process assigned to
/// it inherits process-memory, wall-clock, and "die when handle closes" limits. When the
/// helper terminates (or the session is torn down), every associated child process is
/// terminated with it. <see cref="Dispose"/> closes the kernel handle, which — combined
/// with <see cref="Win32.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE"/> — kills all attached
/// processes synchronously.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsJobObject : IDisposable
{
    private readonly object _sync = new();
    private SafeJobHandle _handle;
    private bool _disposed;

    public WindowsJobObject(JobLimits limits)
    {
        var raw = Win32.CreateJobObject(IntPtr.Zero, null);
        if (raw == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateJobObject failed (Win32 error 0x{Marshal.GetLastWin32Error():X8}).");
        }

        _handle = new SafeJobHandle(raw, ownsHandle: true);
        ApplyLimits(limits);
    }

    public bool IsClosed
    {
        get
        {
            lock (_sync)
            {
                return _disposed || _handle.IsInvalid || _handle.IsClosed;
            }
        }
    }

    public void AssignProcess(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Process handle must be non-zero.", nameof(processHandle));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            if (!Win32.AssignProcessToJobObject(_handle.DangerousGetHandle(), processHandle))
            {
                throw new InvalidOperationException(
                    $"AssignProcessToJobObject failed (Win32 error 0x{Marshal.GetLastWin32Error():X8}).");
            }
        }
    }

    public void Terminate(uint exitCode = 0)
    {
        lock (_sync)
        {
            if (_disposed || _handle.IsInvalid)
            {
                return;
            }

            // Best effort; failure to terminate (e.g. already dead) is not fatal.
            Win32.TerminateJobObject(_handle.DangerousGetHandle(), exitCode);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _handle.Dispose();
        }
    }

    private void ApplyLimits(JobLimits limits)
    {
        var info = new Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();

        var basicLimitFlags = Win32.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                              | Win32.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION;

        if (limits.PerJobUserTimeMilliseconds is { } cpuMs && cpuMs > 0)
        {
            basicLimitFlags |= Win32.JOB_OBJECT_LIMIT_JOB_TIME;
            // PerJobUserTimeLimit is in 100-ns intervals.
            info.BasicLimitInformation.PerJobUserTimeLimit = (long)cpuMs * 10_000L;
        }

        info.BasicLimitInformation.LimitFlags = basicLimitFlags;

        var processMemoryBytes = limits.ProcessMemoryBytes ?? JobLimits.DefaultProcessMemoryBytes;
        if (processMemoryBytes > 0)
        {
            info.BasicLimitInformation.LimitFlags |= Win32.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            info.ProcessMemoryLimit = (UIntPtr)processMemoryBytes;
        }

        var size = Marshal.SizeOf<Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var unmanaged = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, unmanaged, fDeleteOld: false);
            if (!Win32.SetInformationJobObject(
                    _handle.DangerousGetHandle(),
                    Win32.JobObjectExtendedLimitInformation,
                    unmanaged,
                    (uint)size))
            {
                throw new InvalidOperationException(
                    $"SetInformationJobObject failed (Win32 error 0x{Marshal.GetLastWin32Error():X8}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(unmanaged);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsJobObject));
        }
    }

    public sealed record JobLimits(
        ulong? ProcessMemoryBytes = null,
        long? PerJobUserTimeMilliseconds = null)
    {
        /// <summary>2 GB default per spec §14.1.</summary>
        public const ulong DefaultProcessMemoryBytes = 2UL * 1024UL * 1024UL * 1024UL;

        public static JobLimits Default => new(DefaultProcessMemoryBytes, null);
    }

    private sealed class SafeJobHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            return Win32.CloseHandle(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static class Win32
    {
        public const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
        public const uint JOB_OBJECT_LIMIT_JOB_TIME = 0x00000004;
        public const uint JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        public const int JobObjectExtendedLimitInformation = 9;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr hJob,
            int jobObjectInformationClass,
            IntPtr lpJobObjectInformation,
            uint cbJobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
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
