using YukonWinDock.Interop;

namespace YukonWinDock.Services;

public sealed class TaskbarHost(nint window)
{
    private nint parent;
    private (int X, int Y, int Width, int Height)? lastBounds;
    private bool? shown;
    public bool Embedded => parent != 0 && Native.IsWindow(parent) && Native.GetParent(window) == parent;
    public int BoundsUpdates { get; private set; }
    public bool Attach()
    {
        if (Embedded) return true;
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0) return false;
        var original = Native.GetWindowLongPtr(window, -16);
        Native.SetWindowLongPtr(window, -16, new nint((original.ToInt64() & ~0x80000000L) | 0x40000000L));
        Native.SetParent(window, taskbar);
        if (Native.GetParent(window) != taskbar) { Native.SetWindowLongPtr(window, -16, original); return false; }
        parent = taskbar; lastBounds = null; shown = null;
        return true;
    }
    public void Place(AnchorInfo? anchor)
    {
        bool visible = anchor?.Visible == true;
        if (shown != visible) { Native.ShowWindow(window, visible ? 4 : 0); shown = visible; }
        if (anchor == null) return;
        var point = new Native.Point { X = anchor.X, Y = anchor.Y };
        if (Embedded) Native.ScreenToClient(parent, ref point);
        var bounds = (point.X, point.Y, anchor.Width, anchor.Height);
        if (lastBounds == bounds && Native.GetWindowRect(window, out var actual))
        {
            var actualPoint = new Native.Point { X = actual.Left, Y = actual.Top };
            if (Embedded) Native.ScreenToClient(parent, ref actualPoint);
            if (actualPoint.X == point.X && actualPoint.Y == point.Y && actual.Right - actual.Left == anchor.Width && actual.Bottom - actual.Top == anchor.Height) return;
        }
        // Embedded children follow the shell's z-order. Never repeatedly raise
        // a topmost window on mouse-enter or on a timer tick.
        Native.SetWindowPos(window, Embedded ? 0 : new nint(-1), point.X, point.Y, anchor.Width, anchor.Height, 0x0010);
        lastBounds = bounds; BoundsUpdates++;
    }
    public void Detach()
    {
        if (!Embedded) return;
        Native.SetParent(window, 0);
        var style = Native.GetWindowLongPtr(window, -16).ToInt64();
        Native.SetWindowLongPtr(window, -16, new nint((style & ~0x40000000L) | 0x80000000L));
        parent = 0;
    }
}
