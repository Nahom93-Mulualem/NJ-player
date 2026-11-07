using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NJPlayer.App.Views;

/// <summary>
/// Win32 child window host that renders mpv video frames via its window handle (wid).
/// </summary>
public sealed class VideoHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    public IntPtr Hwnd { get; private set; }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        Hwnd = CreateWindowEx(
            0,
            "static",
            "NJPlayerVideoHost",
            WsChild | WsVisible | WsClipChildren,
            0, 0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        return new HandleRef(this, Hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (hwnd.Handle != IntPtr.Zero)
        {
            DestroyWindow(hwnd.Handle);
            Hwnd = IntPtr.Zero;
        }
    }
}

