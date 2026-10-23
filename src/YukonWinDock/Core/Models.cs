using System.Text.Json;

namespace YukonWinDock.Core;

public sealed record MonitorInfo(string Id, string Name, int? Brightness, string Status);
public sealed record FanReading(string Name, double Rpm, string Source);
public sealed record TelemetrySnapshot(DateTimeOffset Timestamp, double DownloadBytesPerSecond,
    double UploadBytesPerSecond, double? CpuUsage, double? CpuGhz, double MemoryPercent,
    IReadOnlyList<FanReading> Fans, string FanStatus, string NetworkName, string CpuSpeedSource);

public sealed class QuickAction
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 2;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowFans { get; set; } = true;
    public bool ShowDownload { get; set; } = true;
    public bool ShowUpload { get; set; } = true;
    public bool ShowCpuSpeed { get; set; } = true;
    public bool ShowCpuUsage { get; set; } = true;
    public bool ShowMemory { get; set; } = true;
    public bool ShowFan1 { get; set; } = true;
    public bool ShowFan2 { get; set; } = true;
    public bool ShowBrightness { get; set; } = true;
    public bool ShowCpuValues { get; set; } = true;
    public bool ShowFanRpm { get; set; }
    public int FanScaleRpm { get; set; } = 6000;
    public bool AutoStartElevated { get; set; } = true;
    public int HorizontalOffset { get; set; }
    public string? SelectedMonitorId { get; set; }
    public string? NetworkInterfaceId { get; set; }
    public List<QuickAction> Actions { get; set; } = [];
}

public static class SettingsStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yukonWinDock");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static AppSettings Load()
    {
        try
        {
            return Decode(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static AppSettings Decode(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return new();
        if (!document.RootElement.TryGetProperty("SettingsVersion", out _) || settings.SettingsVersion < 2)
        {
            settings.ShowDownload = settings.ShowUpload = settings.ShowNetwork;
            settings.ShowCpuSpeed = settings.ShowCpuUsage = settings.ShowCpu;
            settings.ShowFan1 = settings.ShowFan2 = settings.ShowFans;
            settings.SettingsVersion = 2;
        }
        settings.Actions ??= [];
        settings.HorizontalOffset = Math.Clamp(settings.HorizontalOffset, -600, 0);
        settings.FanScaleRpm = Math.Clamp(settings.FanScaleRpm, 1000, 20000);
        return settings;
    }
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public static void Log(Exception ex)
    {
        try { Directory.CreateDirectory(Folder); File.AppendAllText(Path.Combine(Folder, "app.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch { }
    }
}

public static class Formats
{
    public static string Rate(double bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024 * 1024):0.0} GB/s";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024 * 1024):0.0} MB/s";
        return $"{Math.Max(0, bytes) / 1024:0.0} KB/s";
    }
    public static int ToPercent(uint min, uint current, uint max) => max > min
        ? (int)Math.Clamp(Math.Round(((double)current - min) / (max - min) * 100), 0, 100) : 0;
    public static uint ToNative(uint min, uint max, int percent) => min + (uint)Math.Round((max - min) * (Math.Clamp(percent, 0, 100) / 100.0));
}
