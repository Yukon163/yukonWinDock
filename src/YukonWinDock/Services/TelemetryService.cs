using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using YukonWinDock.Core;
using YukonWinDock.Interop;

namespace YukonWinDock.Services;

public sealed class TelemetryService : IDisposable
{
    private long previousIdle, previousTotal;
    private bool cpuPrimed;
    private readonly Dictionary<string, (long Rx, long Tx, double Time)> networkSamples = [];
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private nint query, performanceCounter, frequencyCounter;
    private double baseMhz;
    private IReadOnlyList<FanReading> fans = [];
    private string fanStatus = "正在检测风扇传感器…";
    private double nextFanRead;
    public string? PreferredNetworkId { get; set; }
    public string CpuName { get; private set; } = "CPU";

    [DllImport("iphlpapi.dll")] private static extern uint GetBestInterface(uint destination, out uint index);

    public TelemetryService()
    {
        try
        {
            using var search = new ManagementObjectSearcher("SELECT Name,MaxClockSpeed FROM Win32_Processor");
            using var rows = search.Get();
            foreach (ManagementObject row in rows)
            {
                using (row) { CpuName = row["Name"]?.ToString()?.Trim() ?? "CPU"; baseMhz = Convert.ToDouble(row["MaxClockSpeed"]); }
                break;
            }
        }
        catch (Exception ex) { SettingsStore.Log(ex); }
        if (Native.PdhOpenQuery(null, 0, out query) == 0)
        {
            Native.PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Performance", 0, out performanceCounter);
            Native.PdhAddEnglishCounter(query, @"\Processor Information(_Total)\Processor Frequency", 0, out frequencyCounter);
            Native.PdhCollectQueryData(query);
        }
    }
    public TelemetrySnapshot Sample()
    {
        double? cpu = null;
        if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long total = kernel + user, elapsed = total - previousTotal;
            if (cpuPrimed && elapsed > 0) cpu = Math.Clamp(100.0 * (elapsed - (idle - previousIdle)) / elapsed, 0, 100);
            previousIdle = idle; previousTotal = total; cpuPrimed = true;
        }
        double? ghz = null;
        string speedSource = "暂不可用";
        if (query != 0 && Native.PdhCollectQueryData(query) == 0)
        {
            var ratio = ReadCounter(performanceCounter);
            if (ratio is > 0 && baseMhz > 0)
            { ghz = baseMhz * ratio / 100_000; speedSource = "Windows 性能计数器 · 平均有效频率"; }
            else if (ReadCounter(frequencyCounter) is > 0 and var mhz)
            { ghz = mhz / 1000; speedSource = "Windows Processor Frequency"; }
        }
        var (rx, tx, networkName) = ReadNetwork();
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        Native.GlobalMemoryStatusEx(ref memory);
        if (stopwatch.Elapsed.TotalSeconds >= nextFanRead)
        {
            ReadFans();
            nextFanRead = stopwatch.Elapsed.TotalSeconds + (fans.Count > 0 ? 3 : 30);
        }
        return new(DateTimeOffset.Now, rx, tx, cpu, ghz, memory.Load, fans, fanStatus, networkName, speedSource);
    }
    private static double? ReadCounter(nint counter)
    {
        if (counter == 0 || Native.PdhGetFormattedCounterValue(counter, 0x200 | 0x8000, out _, out var value) != 0 || value.Status > 1) return null;
        return double.IsFinite(value.Value) ? value.Value : null;
    }
    public static NetworkInterface[] Adapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        .ToArray();
    private (double Rx, double Tx, string Name) ReadNetwork()
    {
        try
        {
            var adapters = Adapters();
            NetworkInterface? nic = null;
            if (!string.IsNullOrEmpty(PreferredNetworkId)) nic = adapters.FirstOrDefault(n => n.Id == PreferredNetworkId);
            else if (GetBestInterface(0x01010101, out uint bestIndex) == 0)
                nic = adapters.FirstOrDefault(n => { try { return n.GetIPProperties().GetIPv4Properties()?.Index == bestIndex; } catch { return false; } });
            nic ??= string.IsNullOrEmpty(PreferredNetworkId) ? adapters.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().GatewayAddresses.Count > 0) : null;
            if (nic == null || nic.OperationalStatus != OperationalStatus.Up) return (0, 0, nic == null ? "无活动网络" : $"{nic.Name} · 已断开");
            var stats = nic.GetIPStatistics();
            double now = stopwatch.Elapsed.TotalSeconds;
            double rx = 0, tx = 0;
            if (networkSamples.TryGetValue(nic.Id, out var previous) && now > previous.Time)
            {
                rx = RateDelta(previous.Rx, stats.BytesReceived, now - previous.Time);
                tx = RateDelta(previous.Tx, stats.BytesSent, now - previous.Time);
            }
            networkSamples[nic.Id] = (stats.BytesReceived, stats.BytesSent, now);
            return (rx, tx, nic.Name);
        }
        catch (Exception) { return (0, 0, "网络状态暂不可用"); }
    }
    public static double RateDelta(long previous, long current, double seconds) => seconds <= 0 || current < previous ? 0 : (current - previous) / seconds;

    private void ReadFans()
    {
        var result = FanSensors.Read();
        fans = result.Fans;
        fanStatus = result.Status;
    }
    public void Dispose() { if (query != 0) { Native.PdhCloseQuery(query); query = 0; } }
}
