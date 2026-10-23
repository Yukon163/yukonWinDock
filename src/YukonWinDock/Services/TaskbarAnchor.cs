using System.Runtime.InteropServices;
using YukonWinDock.Interop;

namespace YukonWinDock.Services;

public sealed record AnchorInfo(int X, int Y, int Width, int Height, double Scale, bool Visible, int TrayLeft, int TaskbarTop);

public static class TaskbarAnchor
{
    public static AnchorInfo? Locate(double desiredWidth, int offset = 0)
    {
        nint taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !Native.GetWindowRect(taskbar, out var bar)) return null;
        nint tray = Native.FindWindowEx(taskbar, 0, "TrayNotifyWnd", null);
        bool gotTray = tray != 0 && Native.GetWindowRect(tray, out _);
        Native.GetWindowRect(tray, out var trayRect);
        double scale = Math.Max(96, Native.GetDpiForWindow(taskbar)) / 96.0;
        var mi = new Native.MonitorInfoEx { Size = Marshal.SizeOf<Native.MonitorInfoEx>(), Device = "" };
        Native.GetMonitorInfo(Native.MonitorFromWindow(taskbar, 2), ref mi);
        int height = bar.Bottom - bar.Top;
        int width = (int)Math.Round(desiredWidth * scale);
        // No foreign HWND is moved or subclassed. The widget is an independent tool window.
        int right = gotTray ? trayRect.Left - (int)(6 * scale) : bar.Right - (int)(310 * scale);
        int x = Math.Clamp(right - width + (int)(Math.Min(offset, 0) * scale), bar.Left + 8, Math.Max(bar.Left + 8, right - width));
        bool visible = Native.IsWindowVisible(taskbar) && height > 8 && bar.Top < mi.Monitor.Bottom - 4 && height < 160 * scale;
        nint foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint pid);
        if (foreground != 0 && foreground != taskbar && pid != Environment.ProcessId && Native.GetWindowRect(foreground, out var front))
        {
            // Hide over borderless/fullscreen games; maximized apps end at the work area instead.
            if (front.Left <= mi.Monitor.Left && front.Top <= mi.Monitor.Top && front.Right >= mi.Monitor.Right && front.Bottom >= mi.Monitor.Bottom)
                visible = false;
        }
        return new(x, bar.Top + Math.Max(0, (height - (int)(40 * scale)) / 2), width, (int)(40 * scale), scale, visible, right, bar.Top);
    }
}
