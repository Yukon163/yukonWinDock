using System.Management;
using System.Security.Principal;
using System.Text.Json;
using YukonWinDock.Core;

namespace YukonWinDock.Services;

public sealed record FanProbeResult(IReadOnlyList<FanReading> Fans, string Status, IReadOnlyList<string> Diagnostics);

public static class FanSensors
{
    public static FanProbeResult Read()
    {
        var found = new List<FanReading>();
        var errors = new List<string>();
        bool denied = false;
        void Failure(string source, Exception error)
        {
            denied |= error is UnauthorizedAccessException || error is ManagementException { ErrorCode: ManagementStatus.AccessDenied };
            errors.Add($"{source}: {error.GetType().Name}: {error.Message}");
        }
        // Gen 8/9 Legion exposes RPM as capability IDs; older GameZone methods may
        // still be advertised by WMI but throw NotSupported when invoked.
        try
        {
            using var search = Search(@"root\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");
            using var rows = search.Get();
            foreach (ManagementObject instance in rows)
            {
                using (instance)
                    for (int fan = 1; fan <= 2; fan++)
                        try
                        {
                            using var input = instance.GetMethodParameters("GetFeatureValue");
                            input["IDs"] = 0x04030000u + (uint)fan;
                            using var output = instance.InvokeMethod("GetFeatureValue", input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                            Add(found, output?["value"], $"风扇 {fan}", "Lenovo Capability WMI");
                        }
                        catch (Exception ex) { Failure($"GetFeatureValue(0x0403000{fan})", ex); }
                break;
            }
        }
        catch (Exception ex) { Failure("LENOVO_OTHER_METHOD", ex); }
        if (found.Count == 0)
            try
            {
                using var search = Search(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var rows = search.Get();
                foreach (ManagementObject instance in rows)
                {
                    using (instance)
                        for (int fan = 1; fan <= 2; fan++)
                            try
                            {
                                using var output = instance.InvokeMethod($"GetFan{fan}Speed", null, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                                Add(found, output?["Data"], $"风扇 {fan}", "Lenovo GameZone WMI");
                            }
                            catch (Exception ex) { Failure($"GetFan{fan}Speed", ex); }
                    break;
                }
            }
            catch (Exception ex) { Failure("LENOVO_GAMEZONE_DATA", ex); }
        if (found.Count == 0)
            try
            {
                using var search = Search(@"root\WMI", "SELECT * FROM LENOVO_FAN_METHOD");
                using var rows = search.Get();
                foreach (ManagementObject instance in rows)
                {
                    using (instance)
                        for (byte fan = 0; fan < 2; fan++)
                            try
                            {
                                using var input = instance.GetMethodParameters("Fan_GetCurrentFanSpeed");
                                input["FanID"] = fan;
                                using var output = instance.InvokeMethod("Fan_GetCurrentFanSpeed", input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                                Add(found, output?["CurrentFanSpeed"], $"风扇 {fan + 1}", "Lenovo Fan WMI");
                            }
                            catch (Exception ex) { Failure($"Fan_GetCurrentFanSpeed({fan})", ex); }
                    break;
                }
            }
            catch (Exception ex) { Failure("LENOVO_FAN_METHOD", ex); }
        if (found.Count == 0)
            foreach (string ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
            {
                try
                {
                    using var search = Search(ns, "SELECT Name,Value FROM Sensor WHERE SensorType='Fan'");
                    using var rows = search.Get();
                    foreach (ManagementObject row in rows)
                        using (row) Add(found, row["Value"], row["Name"]?.ToString() ?? "风扇", ns.Split('\\')[1]);
                    if (found.Count > 0) break;
                }
                catch (Exception ex) { errors.Add($"{ns}: {ex.Message}"); }
            }
        bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        string status = found.Count > 0 ? $"{found[0].Source} · 只读转速" : denied && !admin
            ? "联想风扇传感器需要管理员权限；可用下方按钮重启读取。"
            : "当前固件接口未返回转速。诊断已保存，可接入 LibreHardwareMonitor 的 WMI 传感器。";
        var result = new FanProbeResult(found, status, errors);
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(Path.Combine(SettingsStore.Folder, "fan-diagnostics.json"), JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.Now, Administrator = admin, Result = result }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        return result;
    }
    private static ManagementObjectSearcher Search(string ns, string query) => new(ns, query) { Options = { Timeout = TimeSpan.FromSeconds(2) } };
    private static void Add(List<FanReading> result, object? raw, string name, string source)
    {
        if (raw == null) return;
        double rpm = Convert.ToDouble(raw);
        if (double.IsFinite(rpm) && rpm is >= 0 and < 30000) result.Add(new(name, rpm, source));
    }
}
