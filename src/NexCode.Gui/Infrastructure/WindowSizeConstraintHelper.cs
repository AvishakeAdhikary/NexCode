using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace NexCode.Gui.Infrastructure;

internal sealed class WindowSizeConstraintHelper
{
    private const int GwlWndProc = -4;
    private const uint WmGetMinMaxInfo = 0x0024;

    private readonly int _minimumHeight;
    private readonly int _minimumWidth;
    private readonly WndProc _newWindowProc;
    private readonly nint _oldWindowProc;

    private WindowSizeConstraintHelper(Window window, int minimumWidth, int minimumHeight)
    {
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
        _newWindowProc = WindowProc;

        var hwnd = WindowNative.GetWindowHandle(window);
        _oldWindowProc = SetWindowLongPtr(hwnd, GwlWndProc, _newWindowProc);
    }

    public static WindowSizeConstraintHelper Attach(Window window, int minimumWidth, int minimumHeight)
    {
        return new WindowSizeConstraintHelper(window, minimumWidth, minimumHeight);
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmGetMinMaxInfo)
        {
            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMaxInfo.MinimumTrackSize.X = _minimumWidth;
            minMaxInfo.MinimumTrackSize.Y = _minimumHeight;
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return 0;
        }

        return CallWindowProc(_oldWindowProc, hWnd, msg, wParam, lParam);
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, WndProc newProc);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
    private static extern nint CallWindowProc(nint previousWindowProc, nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaximumSize;
        public Point MaximumPosition;
        public Point MinimumTrackSize;
        public Point MaximumTrackSize;
    }
}
