using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using YukonWinDock.Core;

namespace YukonWinDock.Views;

// Fixed cell widths and a retained surface avoid per-sample layout changes.
public sealed class DockStrip : FrameworkElement
{
    private static readonly Brush Text = Frozen("#EAF0F3"), Muted = Frozen("#96A1AD"), Track = Frozen("#3C4752"), Accent = Frozen("#79D5B1"), Background = Frozen("#F21B1F24");
    private static readonly Dictionary<string, (Geometry Geometry, Rect ViewBox)> icons = [];
    private AppSettings settings = new();
    private TelemetrySnapshot? snapshot;
    private MonitorInfo? monitor;
    private string? hovered;
    private readonly ToolTip hoverTip = new() { Focusable = false, IsHitTestVisible = false };
    private readonly TextBlock hoverText = new() { Foreground = Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
    public double DesiredDockWidth => DockLayout.Width(settings);
    public DockStrip()
    {
        Height = 34; Focusable = false; SnapsToDevicePixels = true; UseLayoutRounding = true; Cursor = Cursors.Hand;
        hoverTip.Content = hoverText;
        ToolTipService.SetInitialShowDelay(this, 600);
        ToolTipService.SetBetweenShowDelay(this, 150);
        ToolTipService.SetShowDuration(this, 10000);
        ToolTipService.SetPlacement(this, PlacementMode.Top);
        ToolTipService.SetVerticalOffset(this, -6);
    }
    public void Apply(AppSettings options, TelemetrySnapshot? data, MonitorInfo? display)
    {
        settings = options; snapshot = data; monitor = display;
        if (Width != DesiredDockWidth) Width = DesiredDockWidth;
        if (hovered != null && !DockLayout.Tiles(options).Any(t => t.Key == hovered)) ClearHover();
        if (hovered != null && hoverTip.IsOpen) UpdateHoverText();
        InvalidateVisual();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? key = Hit(e.GetPosition(this).X)?.Key;
        if (hovered == key) return;
        if (key == null) { ClearHover(); return; }
        hovered = key; UpdateHoverText(); ToolTip = hoverTip;
    }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); ClearHover(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { ClearHover(); base.OnMouseLeftButtonDown(e); }
    private void ClearHover() { hoverTip.IsOpen = false; ToolTip = null; hovered = null; }
    private void UpdateHoverText()
    {
        string description = Description(hovered);
        if (hoverText.Text != description) hoverText.Text = description;
    }
    public bool IsBrightnessAt(System.Windows.Point point) => Hit(point.X)?.Key == "brightness";
    private DockTile? Hit(double x) => DockLayout.Tiles(settings).FirstOrDefault(t => x >= t.X && x <= t.X + t.Width);
    private string Description(string? key) => key switch
    {
        "network" => $"{snapshot?.NetworkName ?? "网络"}\n↓ {Formats.Rate(snapshot?.DownloadBytesPerSecond ?? 0)}\n↑ {Formats.Rate(snapshot?.UploadBytesPerSecond ?? 0)}",
        "cpu" => $"CPU · {snapshot?.CpuUsage:0}% · {snapshot?.CpuGhz:0.00} GHz\n绿色 <55% / 黄色 55–84% / 红色 ≥85%\n{snapshot?.CpuSpeedSource}",
        "memory" => $"内存占用 {snapshot?.MemoryPercent:0}%",
        "fan1" or "fan2" => Fan(key == "fan1" ? 0 : 1) is { } fan
            ? $"{fan.Name} · {fan.Rpm:0} RPM\n每格约 {settings.FanScaleRpm / 8} RPM，满格参考 {settings.FanScaleRpm} RPM"
            : snapshot?.FanStatus ?? "风扇传感器正在读取…",
        "brightness" => $"{monitor?.Name ?? "外接显示器"} · {monitor?.Brightness}%\n滚轮每格调节 5% · 点击展开",
        _ => "yukonWinDock · 点击展开 / 再次点击收起"
    };
    private FanReading? Fan(int index)
        => snapshot?.Fans.FirstOrDefault(f => f.Name == $"风扇 {index + 1}")
            ?? snapshot?.Fans.Where(f => !f.Name.StartsWith("风扇 ", StringComparison.Ordinal)).ElementAtOrDefault(index);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight), 5, 5);
        foreach (var tile in DockLayout.Tiles(settings))
        {
            double x = tile.X;
            switch (tile.Key)
            {
                case "network":
                    var net = new List<(string Text, Brush Color)>();
                    if (settings.ShowDownload) net.Add(("↓ " + Rate(snapshot?.DownloadBytesPerSecond), Text));
                    if (settings.ShowUpload) net.Add(("↑ " + Rate(snapshot?.UploadBytesPerSecond), Muted));
                    Lines(dc, x, net, 10.5); break;
                case "cpu":
                    var cpuBrush = Frozen(DockLayout.LoadColor(snapshot?.CpuUsage));
                    Icon(dc, "microchip", x, 9, 16, cpuBrush);
                    if (settings.ShowCpuValues)
                    {
                        var cpu = new List<(string Text, Brush Color)>();
                        if (settings.ShowCpuSpeed) cpu.Add((snapshot?.CpuGhz is { } ghz ? $"{ghz:0.00}G" : "—G", Text));
                        if (settings.ShowCpuUsage) cpu.Add((snapshot?.CpuUsage is { } use ? $"{use:0}%" : "—%", cpuBrush));
                        Lines(dc, x + 21, cpu, 10.5);
                    }
                    break;
                case "memory":
                    Icon(dc, "memory", x, 10, 14, Muted);
                    Label(dc, snapshot == null ? "—%" : $"{snapshot.MemoryPercent:0}%", x + 19, 10, Text, 10.5); break;
                case "fan1": case "fan2":
                    var fan = Fan(tile.Key == "fan1" ? 0 : 1);
                    var fanBrush = Frozen(DockLayout.LoadColor(fan == null ? null : fan.Rpm / settings.FanScaleRpm * 100));
                    Icon(dc, "fan", x, 9, 16, fan == null ? Muted : Text);
                    Meter(dc, x + 21, 4, fan?.Rpm, fanBrush);
                    if (settings.ShowFanRpm) Label(dc, fan is null ? "—" : $"{fan.Rpm:0}", x + 33, 10, Text, 10);
                    break;
                case "brightness":
                    Icon(dc, "sun", x, 10, 14, Accent);
                    Label(dc, monitor?.Brightness is { } value ? $"{value}%" : "—%", x + 19, 10, Text, 10.5); break;
                default: Label(dc, "Y", x + 4, 7, Accent, 15); break;
            }
        }
    }
    private void Meter(DrawingContext dc, double x, double y, double? rpm, Brush brush)
    {
        var pen = new Pen(Track, 1); pen.Freeze();
        dc.DrawRoundedRectangle(null, pen, new Rect(x, y, 7, 26), 1.5, 1.5);
        int count = DockLayout.FanBlocks(rpm, settings.FanScaleRpm);
        for (int row = 0; row < 8; row++)
            dc.DrawRectangle(row < count ? brush : Track, null, new Rect(x + 2, y + 22 - row * 2.75, 3, 1.75));
        if (rpm == null) dc.DrawRectangle(Muted, null, new Rect(x + 1.5, y + 12, 4, 1));
    }
    private void Lines(DrawingContext dc, double x, List<(string Text, Brush Color)> lines, double size)
    { for (int i = 0; i < lines.Count; i++) Label(dc, lines[i].Text, x, lines.Count == 1 ? 10 : 3.5 + i * 13, lines[i].Color, size); }
    private void Label(DrawingContext dc, string text, double x, double y, Brush brush, double size)
        => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new System.Windows.Point(x, y));
    private static string Rate(double? value) => value is null ? "— KB/s" : Formats.Rate(value.Value);
    internal static void Icon(DrawingContext dc, string name, double x, double y, double size, Brush brush)
    {
        if (!icons.TryGetValue(name, out var icon))
        {
            using var stream = System.Windows.Application.GetResourceStream(new Uri($"/yukonWinDock;component/Assets/Icons/{name}.svg", UriKind.Relative))!.Stream;
            var root = XDocument.Load(stream).Root!;
            var box = root.Attribute("viewBox")!.Value.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var paths = new GeometryGroup();
            foreach (var path in root.Descendants().Where(e => e.Name.LocalName == "path")) paths.Children.Add(Geometry.Parse(path.Attribute("d")!.Value));
            paths.Freeze(); icon = (paths, new Rect(box[0], box[1], box[2], box[3])); icons[name] = icon;
        }
        double scale = size / Math.Max(icon.ViewBox.Width, icon.ViewBox.Height);
        dc.PushTransform(new TranslateTransform(x + (size - icon.ViewBox.Width * scale) / 2, y + (size - icon.ViewBox.Height * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale)); dc.DrawGeometry(brush, null, icon.Geometry); dc.Pop(); dc.Pop();
    }
    private static Brush Frozen(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
}
