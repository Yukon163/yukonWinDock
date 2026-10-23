using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using YukonWinDock.Core;
using YukonWinDock.Interop;
using YukonWinDock.Services;

namespace YukonWinDock.Views;

public partial class DockWindow : Window
{
    private readonly DispatcherTimer anchorTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private nint hwnd;
    private bool wheelBusy;
    public DockWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, Native.GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80);
            anchorTimer.Tick += (_, _) => Position(); anchorTimer.Start(); Position();
        };
        App.Current.Updated += Update;
        Closed += (_, _) => { anchorTimer.Stop(); App.Current.Updated -= Update; };
        RefreshLayout();
    }
    public void RefreshLayout()
    {
        var s = App.Current.Settings;
        NetworkGroup.Visibility = s.ShowDownload || s.ShowUpload ? Visibility.Visible : Visibility.Collapsed;
        Download.Visibility = s.ShowDownload ? Visibility.Visible : Visibility.Collapsed;
        Upload.Visibility = s.ShowUpload ? Visibility.Visible : Visibility.Collapsed;
        CpuGroup.Visibility = s.ShowCpuSpeed || s.ShowCpuUsage ? Visibility.Visible : Visibility.Collapsed;
        CpuSpeed.Visibility = s.ShowCpuSpeed ? Visibility.Visible : Visibility.Collapsed;
        CpuLoad.Visibility = s.ShowCpuUsage ? Visibility.Visible : Visibility.Collapsed;
        MemoryGroup.Visibility = s.ShowMemory ? Visibility.Visible : Visibility.Collapsed;
        FanGroup.Visibility = s.ShowFan1 || s.ShowFan2 ? Visibility.Visible : Visibility.Collapsed;
        FanOne.Visibility = s.ShowFan1 ? Visibility.Visible : Visibility.Collapsed;
        FanTwo.Visibility = s.ShowFan2 ? Visibility.Visible : Visibility.Collapsed;
        BrightnessGroup.Visibility = s.ShowBrightness ? Visibility.Visible : Visibility.Collapsed;
        Width = 12 + (s.ShowDownload || s.ShowUpload ? 101 : 0) + (s.ShowCpuSpeed || s.ShowCpuUsage ? 83 : 0) + (s.ShowMemory ? 66 : 0) + (s.ShowFan1 || s.ShowFan2 ? 100 : 0) + (s.ShowBrightness ? 61 : 0);
        EmptyLauncher.Visibility = Width == 12 ? Visibility.Visible : Visibility.Collapsed;
        Width = Math.Max(Width, 32); Position();
    }
    private void Position()
    {
        if (hwnd == 0) return;
        var anchor = TaskbarAnchor.Locate(Width, App.Current.Settings.HorizontalOffset);
        if (anchor == null) { Opacity = 0; IsHitTestVisible = false; return; }
        Opacity = anchor.Visible ? 1 : 0; IsHitTestVisible = anchor.Visible;
        Native.SetWindowPos(hwnd, new nint(-1), anchor.X, anchor.Y, anchor.Width, anchor.Height, 0x0050);
    }
    private void Update()
    {
        var app = App.Current;
        if (app.Snapshot is { } s)
        {
            Download.Text = "↓ " + Formats.Rate(s.DownloadBytesPerSecond);
            Upload.Text = "↑ " + Formats.Rate(s.UploadBytesPerSecond);
            CpuSpeed.Text = s.CpuGhz is { } ghz ? $"{ghz:0.00} GHz" : "— GHz";
            CpuLoad.Text = s.CpuUsage is { } usage ? $"CPU  {usage:0}%" : "CPU  —%";
            MemoryText.Text = $"{s.MemoryPercent:0}%";
            FanOne.Text = s.Fans.Count > 0 ? $"F1 {s.Fans[0].Rpm:0}" : "F1 — RPM";
            FanTwo.Text = s.Fans.Count > 1 ? $"F2 {s.Fans[1].Rpm:0}" : "F2 — RPM";
            FanGroup.ToolTip = s.Fans.Count > 0 ? string.Join("\n", s.Fans.Select(f => $"{f.Name}: {f.Rpm:0} RPM")) : s.FanStatus;
            NetworkGroup.ToolTip = $"{s.NetworkName}\n每秒采样；下载 / 上传；1024 进位";
            CpuGroup.ToolTip = s.CpuSpeedSource;
        }
        BrightnessText.Text = app.SelectedMonitor?.Brightness is { } brightness ? $"☼ {brightness}%" : "☼ —%";
    }
    private void OpenPanel(object sender, MouseButtonEventArgs e) => App.Current.TogglePanel();
    private async void AdjustBrightness(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (wheelBusy || App.Current.SelectedMonitor is not { Brightness: { } current } monitor) return;
        wheelBusy = true;
        try { await App.Current.SetBrightnessAsync(monitor.Id, Math.Clamp(current + Math.Sign(e.Delta) * 5, 0, 100)); }
        catch (Exception ex) { Surface.ToolTip = ex.Message; }
        finally { wheelBusy = false; }
    }
}
