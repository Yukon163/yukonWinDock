using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using YukonWinDock.Core;
using YukonWinDock.Interop;

namespace YukonWinDock.Services;

public sealed class BrightnessService
{
    // DDC calls are serialized; handles are reacquired per operation and always released.
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IReadOnlyList<MonitorInfo>> ReadAsync()
    {
        await gate.WaitAsync();
        try { return await Task.Run(Read); } finally { gate.Release(); }
    }
    private static List<MonitorInfo> Read()
    {
        var result = new List<MonitorInfo>();
        var names = FriendlyNames();
        Visit((id, name, handle) =>
        {
            string model = id.Split('\\').ElementAtOrDefault(1) ?? "";
            if (names.TryGetValue(model, out var friendly)) name = friendly;
            bool ok = Native.GetMonitorBrightness(handle, out uint min, out uint value, out uint max) && max > min;
            result.Add(new(id, name, ok ? Formats.ToPercent(min, value, max) : null,
                ok ? "DDC/CI · 硬件亮度" : "当前连接不支持 DDC/CI 亮度控制"));
        });
        return result;
    }
    private static Dictionary<string, string> FriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName,UserFriendlyName FROM WmiMonitorID WHERE Active=True");
            using var rows = search.Get();
            foreach (ManagementObject row in rows)
                using (row)
                    if (row["UserFriendlyName"] is ushort[] chars)
                    {
                        string friendly = new(chars.Where(c => c != 0).Select(c => (char)c).ToArray());
                        string model = row["InstanceName"]?.ToString()?.Split('\\').ElementAtOrDefault(1) ?? "";
                        if (friendly.Length > 0 && model.Length > 0) names[model] = friendly;
                    }
        }
        catch { }
        return names;
    }
    public async Task<int> SetAsync(string id, int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                int? actual = null;
                Visit((candidate, unusedName, handle) =>
                {
                    if (candidate != id) return;
                    if (!Native.GetMonitorBrightness(handle, out uint min, out _, out uint max) || max <= min)
                        throw new InvalidOperationException("显示器不支持亮度控制，请检查 DDC/CI 设置和连接线。");
                    if (!Native.SetMonitorBrightness(handle, Formats.ToNative(min, max, percent)))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "显示器未接受亮度设置。");
                    if (!Native.GetMonitorBrightness(handle, out min, out uint current, out max))
                        throw new InvalidOperationException("已发送设置，但无法读取确认，请刷新显示器。");
                    actual = Formats.ToPercent(min, current, max);
                });
                return actual ?? throw new InvalidOperationException("显示器已断开，请刷新显示器列表。");
            });
        }
        finally { gate.Release(); }
    }
    private static void Visit(Action<string, string, nint> action)
    {
        // Never throw through a native callback.
        Exception? failure = null;
        Native.EnumDisplayMonitors(0, 0, (h, _, _, _) =>
        {
            Native.PhysicalMonitor[]? physical = null;
            try
            {
                var info = new Native.MonitorInfoEx { Size = Marshal.SizeOf<Native.MonitorInfoEx>(), Device = "" };
                if (!Native.GetMonitorInfo(h, ref info)) return true;
                var display = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
                Native.EnumDisplayDevices(info.Device, 0, ref display, 0);
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(h, out uint count) || count is 0 or > 16) return true;
                physical = new Native.PhysicalMonitor[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(h, count, physical)) { physical = null; return true; }
                for (int i = 0; i < physical.Length; i++)
                {
                    string name = string.IsNullOrWhiteSpace(display.DeviceString) || display.DeviceString.Contains("Generic", StringComparison.OrdinalIgnoreCase)
                        ? physical[i].Description : display.DeviceString;
                    string identity = string.IsNullOrWhiteSpace(display.DeviceId) ? info.Device : display.DeviceId;
                    action($"{identity}#{i}", name, physical[i].Handle);
                }
            }
            catch (Exception ex) { failure = ex; return false; }
            finally { if (physical != null) Native.DestroyPhysicalMonitors((uint)physical.Length, physical); }
            return true;
        }, 0);
        if (failure != null) throw failure;
    }
}
