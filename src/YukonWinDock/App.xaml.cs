using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using YukonWinDock.Core;
using YukonWinDock.Services;
using YukonWinDock.Views;
using Forms = System.Windows.Forms;

namespace YukonWinDock;

public partial class App : System.Windows.Application
{
    private Mutex? singleInstance;
    private EventWaitHandle? showEvent, quitEvent;
    private RegisteredWaitHandle? showWait, quitWait;
    private readonly CancellationTokenSource cancellation = new();
    private Forms.NotifyIcon? tray;
    private TelemetryService? telemetry;
    private DockWindow? dock;
    private ControlPanel? panel;
    public AppSettings Settings { get; private set; } = new();
    public BrightnessService Brightness { get; } = new();
    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];
    public TelemetrySnapshot? Snapshot { get; private set; }
    public string CpuName => telemetry?.CpuName ?? "正在读取 CPU…";
    public bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public event Action? Updated;
    public static new App Current => (App)System.Windows.Application.Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--diagnose")
        {
            try
            {
                using var probe = new TelemetryService();
                probe.Sample();
                await Task.Delay(1100);
                var result = new { Timestamp = DateTimeOffset.Now, Cpu = probe.CpuName, Snapshot = probe.Sample(), Monitors = await Brightness.ReadAsync(), Anchor = TaskbarAnchor.Locate(336), Administrator = IsAdministrator };
                string output = Path.GetFullPath(e.Args.ElementAtOrDefault(1) ?? "diagnostics.json");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex) { SettingsStore.Log(ex); Shutdown(1); }
            return;
        }
        if (e.Args.FirstOrDefault() == "--quit")
        {
            try { EventWaitHandle.OpenExisting(@"Local\YukonWinDock.Quit").Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        singleInstance = new Mutex(false, @"Local\YukonWinDock.Instance");
        bool acquired;
        try { acquired = singleInstance.WaitOne(e.Args.Contains("--restarted") ? 6000 : 0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { using var existing = EventWaitHandle.OpenExisting(@"Local\YukonWinDock.Show"); existing.Set(); } catch { }
            singleInstance.Dispose(); singleInstance = null; Shutdown(); return;
        }
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\YukonWinDock.Show");
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\YukonWinDock.Quit");
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(ShowPanel), null, -1, false);
        quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, (_, _) => Dispatcher.BeginInvoke(() => Shutdown()), null, -1, false);
        DispatcherUnhandledException += (_, args) => { SettingsStore.Log(args.Exception); args.Handled = true; Forms.MessageBox.Show(args.Exception.Message, "yukonWinDock"); };
        Settings = SettingsStore.Load();
        CreateTray();
        dock = new DockWindow(); MainWindow = dock; dock.Show();
        telemetry = await Task.Run(() => new TelemetryService { PreferredNetworkId = Settings.NetworkInterfaceId });
        _ = RunTelemetryAsync(cancellation.Token);
        _ = RunBrightnessAsync(cancellation.Token);
        if (e.Args.Contains("--panel")) { panel = new ControlPanel(); panel.KeepOpen.IsChecked = true; ShowPanel(); }
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开控制面板", null, (_, _) => Dispatcher.Invoke(ShowPanel));
        menu.Items.Add("刷新显示器", null, async (_, _) => await RefreshMonitorsAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 yukonWinDock", null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "yukonWinDock.exe");
        tray = new Forms.NotifyIcon { Text = "yukonWinDock · 点击打开控制面板", Icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath) ?? System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(TogglePanel); };
    }
    private async Task RunTelemetryAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    telemetry!.PreferredNetworkId = Settings.NetworkInterfaceId;
                    Snapshot = await Task.Run(telemetry.Sample, token);
                    Updated?.Invoke();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { SettingsStore.Log(ex); }
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task RunBrightnessAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshMonitorsAsync();
                await Task.Delay(TimeSpan.FromSeconds(15), token);
            }
        }
        catch (OperationCanceledException) { }
    }
    public MonitorInfo? SelectedMonitor => Monitors.FirstOrDefault(m => m.Id == Settings.SelectedMonitorId)
        ?? Monitors.FirstOrDefault(m => m.Brightness.HasValue);
    public async Task RefreshMonitorsAsync()
    {
        try { Monitors = await Brightness.ReadAsync(); Updated?.Invoke(); }
        catch (Exception ex) { SettingsStore.Log(ex); }
    }
    public async Task<int> SetBrightnessAsync(string id, int percent)
    {
        int actual = await Brightness.SetAsync(id, percent);
        Monitors = Monitors.Select(m => m.Id == id ? m with { Brightness = actual } : m).ToArray();
        Updated?.Invoke(); return actual;
    }
    public void ShowPanel()
    {
        panel ??= new ControlPanel();
        panel.Place(dock);
        panel.Show(); panel.Activate();
    }
    public void TogglePanel()
    {
        if (panel != null && !PanelTogglePolicy.ShouldShow(panel.IsVisible, panel.SinceAutoDismiss)) { panel.Hide(); return; }
        ShowPanel();
    }
    public void SaveSettings() { SettingsStore.Save(Settings); dock?.RefreshLayout(); Updated?.Invoke(); }
    public bool AutoStart
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("yukonWinDock") != null; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("yukonWinDock", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("yukonWinDock", false);
        }
    }
    public void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--panel --restarted" });
            Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        cancellation.Cancel();
        showWait?.Unregister(null); quitWait?.Unregister(null);
        showEvent?.Dispose(); quitEvent?.Dispose();
        tray?.Dispose();
        if (singleInstance != null) { singleInstance.ReleaseMutex(); singleInstance.Dispose(); }
        base.OnExit(e);
    }
}
