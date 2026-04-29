using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.UI.Xaml;

namespace NexCode.Gui.BackgroundService;

/// <summary>
/// Spec §37 — global hotkey for summoning NexCode.
///
/// Registers <c>Ctrl+Shift+N</c> by default (configurable via constructor). On
/// press, brings the hidden NexCode main window to the foreground or starts it
/// if not running. The implementation uses Win32 <c>RegisterHotKey</c> on a
/// dedicated message-loop thread so it works regardless of WinUI focus.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class GlobalHotkeyService(Window owner, Action onTriggered) : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const int HOTKEY_ID = 0xC0DE;

    // Virtual-key codes
    private const uint VK_N = 0x4E;

    private Thread? _pumpThread;
    private uint _threadId;
    private bool _disposed;

    public Window Owner { get; } = owner;

    public Action OnTriggered { get; } = onTriggered;

    public uint Modifiers { get; set; } = MOD_CONTROL | MOD_SHIFT;

    public uint VirtualKey { get; set; } = VK_N;

    public void Register()
    {
        if (_pumpThread is not null)
        {
            return;
        }

        _pumpThread = new Thread(MessagePump)
        {
            IsBackground = true,
            Name = "NexCode.GlobalHotkey"
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();
    }

    public void Unregister()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
    }

    private void MessagePump()
    {
        _threadId = GetCurrentThreadId();
        if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID, Modifiers, VirtualKey))
        {
            return;
        }

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
                {
                    try
                    {
                        OnTriggered();
                    }
                    catch
                    {
                        // hotkey handlers must not crash the message pump
                    }
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
