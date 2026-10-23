namespace YukonWinDock.Core;

public sealed record DockTile(string Key, double X, double Width);

public static class DockLayout
{
    public static IReadOnlyList<DockTile> Tiles(AppSettings s)
    {
        var result = new List<DockTile>();
        double x = 7;
        void Add(string key, double width) { if (result.Count > 0) x += 8; result.Add(new(key, x, width)); x += width; }
        if (s.ShowDownload || s.ShowUpload) Add("network", 82);
        if (s.ShowCpuSpeed || s.ShowCpuUsage) Add("cpu", s.ShowCpuValues ? 61 : 18);
        if (s.ShowMemory) Add("memory", 45);
        if (s.ShowFan1) Add("fan1", s.ShowFanRpm ? 60 : 29);
        if (s.ShowFan2) Add("fan2", s.ShowFanRpm ? 60 : 29);
        if (s.ShowBrightness) Add("brightness", 46);
        if (result.Count == 0) Add("launcher", 18);
        return result;
    }
    public static double Width(AppSettings settings) { var last = Tiles(settings)[^1]; return last.X + last.Width + 7; }
    public static int FanBlocks(double? rpm, int scale) => rpm is not { } value || !double.IsFinite(value) || value <= 0
        ? 0 : (int)Math.Clamp(Math.Ceiling(value / Math.Max(1000, scale) * 8), 1, 8);
    public static string LoadColor(double? percent) => percent is not { } value || !double.IsFinite(value) ? "#88939F"
        : value >= 85 ? "#F27D82" : value >= 55 ? "#E4BB6C" : "#79D5B1";
}
