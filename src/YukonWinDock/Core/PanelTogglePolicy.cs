namespace YukonWinDock.Core;

public static class PanelTogglePolicy
{
    // A tray click can deactivate the popup before NotifyIcon.MouseClick arrives.
    // Treat the two events from that single gesture as one close action.
    public static bool ShouldShow(bool visible, TimeSpan sinceAutoDismiss) => !visible && sinceAutoDismiss >= TimeSpan.FromMilliseconds(350);
}
