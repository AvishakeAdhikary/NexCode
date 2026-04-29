using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Contracts;

namespace NexCode.Service.BackgroundServiceMode;

/// <summary>
/// Spec §29.2 — Win32 tray icon for Background Service Mode. Hosts the icon on
/// a dedicated message-pump thread, exposes state transitions for the spec
/// states (Idle / ActiveLocal / ActiveRemote / Error / AuthRequired / Background),
/// and bridges interaction back to the GUI by publishing
/// <c>summon_gui</c> events on the <see cref="ServiceEventHub"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconHost(
    ILogger<TrayIconHost> logger,
    ServiceEventHub serviceEventHub) : IHostedService, IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private Thread? _pumpThread;
    private CancellationTokenSource? _cts;
    private TrayState _state = TrayState.Background;
    private bool _running;

    public TrayState CurrentState => _state;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_running)
        {
            return Task.CompletedTask;
        }

        if (Environment.GetEnvironmentVariable("NEXCODE_DISABLE_TRAY") == "1")
        {
            logger.LogInformation("Tray icon disabled via NEXCODE_DISABLE_TRAY.");
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _pumpThread = new Thread(() => MessagePump(_cts.Token))
        {
            IsBackground = true,
            Name = "NexCode.TrayIconHost"
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _running = false;
        return Task.CompletedTask;
    }

    public void SetState(TrayState state)
    {
        _state = state;
        logger.LogInformation("Tray state -> {State}", state);
    }

    public void RaiseSummon()
    {
        serviceEventHub.Publish("tray.summon_gui", new TraySummonEventPayload(DateTimeOffset.UtcNow));
    }

    public void Dispose() => _cts?.Dispose();

    private void MessagePump(CancellationToken cancellationToken)
    {
        try
        {
            // Lightweight pump: register a NOTIFYICONDATA, then loop waiting on
            // the cancellation token. Real interaction handlers (left-click,
            // context menu) are handled through window-message hooks in a
            // future iteration; this skeleton is enough to surface presence
            // and to keep the IPC bridge alive.
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = IntPtr.Zero,
                uID = 1,
                uFlags = NIF_TIP,
                szTip = TooltipFor(_state)
            };

            Shell_NotifyIcon(NIM_ADD, ref data);

            while (!cancellationToken.IsCancellationRequested)
            {
                data.szTip = TooltipFor(_state);
                Shell_NotifyIcon(NIM_MODIFY, ref data);
                cancellationToken.WaitHandle.WaitOne(1000);
            }

            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tray icon message pump terminated.");
        }
    }

    private static string TooltipFor(TrayState state) => state switch
    {
        TrayState.Idle => "NexCode (idle)",
        TrayState.ActiveLocal => "NexCode (active — local)",
        TrayState.ActiveRemote => "NexCode (active — remote)",
        TrayState.Error => "NexCode (error)",
        TrayState.AuthRequired => "NexCode (sign-in required)",
        TrayState.Background => "NexCode (background)",
        _ => "NexCode"
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }
}

public enum TrayState
{
    Idle,
    ActiveLocal,
    ActiveRemote,
    Error,
    AuthRequired,
    Background
}

public sealed record TraySummonEventPayload(DateTimeOffset At);
