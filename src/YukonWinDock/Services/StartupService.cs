using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace YukonWinDock.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string TaskName => "yukonWinDock-" + WindowsIdentity.GetCurrent().User!.Value;
    private static bool Admin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private static dynamic Scheduler() { dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!; service.Connect(); return service; }
    public static bool Scheduled
    {
        get
        {
            dynamic? service = null;
            try { return QueryScheduled(() => { service = Scheduler(); return (bool)service.GetFolder(@"\").GetTask(TaskName).Enabled; }); }
            finally { if (service != null) Marshal.FinalReleaseComObject(service); }
        }
    }
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return Scheduled || key?.GetValue("yukonWinDock") != null; }
    }
    public static string Description => !Enabled ? "关闭 · 登录后手动启动" : Scheduled
        ? "已开启 · 登录后自动以管理员运行，可读取风扇"
        : "已开启 · 登录后普通权限运行";
    public static async Task SetEnabledAsync(bool enabled, bool elevated)
    {
        if (!Admin && (enabled && elevated || Scheduled))
        {
            using var worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
                UseShellExecute = true, Verb = "runas", Arguments = $"--set-startup {(enabled ? "on" : "off")} {(elevated ? "--elevated-startup" : "")}" });
            if (worker == null) throw new InvalidOperationException("未启动自启设置程序。");
            await worker.WaitForExitAsync();
            if (worker.ExitCode != 0) throw new InvalidOperationException("未能保存自启动设置。请查看应用日志。");
        }
        else await Task.Run(() => SetEnabled(enabled, elevated));
    }
    public static void SetEnabled(bool enabled, bool elevated)
    {
        dynamic? service = null;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        try
        {
            service = Scheduler(); dynamic folder = service.GetFolder(@"\");
            if (enabled && elevated)
            {
                if (!Admin) throw new UnauthorizedAccessException("管理员自启动需要一次管理员确认。");
                string sid = WindowsIdentity.GetCurrent().User!.Value;
                dynamic task = service.NewTask(0);
                task.RegistrationInfo.Description = "yukonWinDock：登录后显示任务栏状态，并只读获取风扇转速。";
                task.Principal.UserId = sid; task.Principal.LogonType = 3; task.Principal.RunLevel = 1;
                task.Settings.Enabled = true; task.Settings.DisallowStartIfOnBatteries = false;
                task.Settings.StopIfGoingOnBatteries = false; task.Settings.StartWhenAvailable = true;
                task.Settings.ExecutionTimeLimit = "PT0S"; task.Settings.MultipleInstances = 2;
                dynamic trigger = task.Triggers.Create(9); trigger.UserId = sid; trigger.Delay = "PT5S";
                dynamic action = task.Actions.Create(0); action.Path = Environment.ProcessPath!;
                action.WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
                folder.RegisterTaskDefinition(TaskName, task, 6, sid, null, 3, null);
                key.DeleteValue("yukonWinDock", false);
            }
            else
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (Exception ex) when (IsMissingTask(ex)) { }
                if (enabled) key.SetValue("yukonWinDock", CommandFor(Environment.ProcessPath!));
                else key.DeleteValue("yukonWinDock", false);
            }
        }
        finally { if (service != null) Marshal.FinalReleaseComObject(service); }
    }
    public static string CommandFor(string path) => $"\"{path}\"";
    // COM IDispatch HRESULTs may be translated to FileNotFoundException by
    // the C# runtime binder, rather than remaining COMException instances.
    public static bool IsMissingTask(Exception error) => error.HResult is unchecked((int)0x80070002) or unchecked((int)0x8004130F);
    public static bool QueryScheduled(Func<bool> lookup)
    {
        try { return lookup(); }
        catch (Exception ex) when (IsMissingTask(ex)) { return false; }
    }
}
