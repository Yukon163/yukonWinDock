using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Text.Json;
using YukonWinDock.Core;
using YukonWinDock.Interop;
using YukonWinDock.Services;

namespace YukonWinDock.Views;

public partial class DockWindow : Window
{
    private readonly DispatcherTimer anchorTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private nint hwnd;
    private TaskbarHost? host;
    private bool wheelBusy;
    public DockWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, Native.GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80);
            host = new TaskbarHost(hwnd);
            host.Attach();
            anchorTimer.Tick += (_, _) => Position(); anchorTimer.Start(); Position();
        };
        App.Current.Updated += Update;
        Closed += (_, _) => { anchorTimer.Stop(); App.Current.Updated -= Update; };
        RefreshLayout();
    }
    public void RefreshLayout()
    {
        Strip.Apply(App.Current.Settings, App.Current.Snapshot, App.Current.SelectedMonitor);
        Width = Strip.DesiredDockWidth; Position();
    }
    private void Position()
    {
        if (hwnd == 0 || host == null) return;
        host.Attach();
        var anchor = TaskbarAnchor.Locate(Width, App.Current.Settings.HorizontalOffset, host.Embedded);
        int previous = host.BoundsUpdates;
        host.Place(anchor);
        if (host.BoundsUpdates != previous)
        {
            try
            {
                Native.GetWindowRect(hwnd, out var rect);
                Directory.CreateDirectory(SettingsStore.Folder);
                File.WriteAllText(Path.Combine(SettingsStore.Folder, "dock-state.json"), JsonSerializer.Serialize(new {
                    Timestamp = DateTimeOffset.Now, host.Embedded, host.BoundsUpdates, Window = hwnd.ToInt64(), Parent = Native.GetParent(hwnd).ToInt64(),
                    X = rect.Left, Y = rect.Top, Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top
                }));
            }
            catch { }
        }
    }
    private void Update() => Strip.Apply(App.Current.Settings, App.Current.Snapshot, App.Current.SelectedMonitor);
    public void Detach() => host?.Detach();
    private void OpenPanel(object sender, MouseButtonEventArgs e) => App.Current.TogglePanel();
    private async void AdjustBrightness(object sender, MouseWheelEventArgs e)
    {
        if (!Strip.IsBrightnessAt(e.GetPosition(Strip))) return;
        e.Handled = true;
        if (wheelBusy || App.Current.SelectedMonitor is not { Brightness: { } current } monitor) return;
        wheelBusy = true;
        try { await App.Current.SetBrightnessAsync(monitor.Id, Math.Clamp(current + Math.Sign(e.Delta) * 5, 0, 100)); }
        catch (Exception ex) { Strip.ToolTip = ex.Message; }
        finally { wheelBusy = false; }
    }
}
