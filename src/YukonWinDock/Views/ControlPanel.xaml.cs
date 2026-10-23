using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Diagnostics;
using YukonWinDock.Core;
using YukonWinDock.Services;
using YukonWinDock.Interop;

namespace YukonWinDock.Views;

public partial class ControlPanel : Window
{
    private bool syncing, applying;
    private (string Id, int Value)? pending;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string monitorSignature = "";
    private long autoDismissTimestamp;
    public TimeSpan SinceAutoDismiss => autoDismissTimestamp == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(autoDismissTimestamp);
    public ControlPanel()
    {
        InitializeComponent();
        syncing = true;
        var settings = App.Current.Settings;
        ShowDownload.IsChecked = settings.ShowDownload; ShowUpload.IsChecked = settings.ShowUpload;
        ShowCpuSpeed.IsChecked = settings.ShowCpuSpeed; ShowCpuUsage.IsChecked = settings.ShowCpuUsage;
        ShowMemory.IsChecked = settings.ShowMemory; ShowFan1.IsChecked = settings.ShowFan1;
        ShowFan2.IsChecked = settings.ShowFan2; ShowBrightness.IsChecked = settings.ShowBrightness;
        OffsetSlider.Value = settings.HorizontalOffset;
        Startup.IsChecked = StartupService.Enabled;
        StartupElevated.IsChecked = Startup.IsChecked == true ? StartupService.Scheduled : settings.AutoStartElevated;
        StartupStatus.Text = StartupService.Description;
        CpuValues.IsChecked = settings.ShowCpuValues; FanRpm.IsChecked = settings.ShowFanRpm;
        FanScale.Text = settings.FanScaleRpm.ToString();
        var choices = new[] { new AdapterChoice(null, "自动 · 默认路由网卡") }.Concat(TelemetryService.Adapters().Select(n => new AdapterChoice(n.Id, n.Name))).ToArray();
        NetworkChoice.ItemsSource = choices;
        NetworkChoice.SelectedItem = choices.FirstOrDefault(n => n.Id == settings.NetworkInterfaceId) ?? choices[0];
        syncing = false;
        debounce.Tick += async (_, _) => { debounce.Stop(); await ApplyPendingAsync(); };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && KeepOpen.IsChecked != true && !OwnedWindows.Cast<Window>().Any(w => w.IsVisible))
            { autoDismissTimestamp = Stopwatch.GetTimestamp(); Hide(); }
        });
        IsVisibleChanged += (_, _) => { if (IsVisible) Update(); };
        App.Current.Updated += Update;
        RenderActions(); Update();
    }
    public void Place(DockWindow? dock)
    {
        var screen = System.Windows.Forms.Screen.FromHandle(dock == null ? 0 : new WindowInteropHelper(dock).Handle);
        double scale = PresentationSource.FromVisual((Window?)dock ?? this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var area = screen.WorkingArea;
        Height = Math.Min(690, area.Height / scale - 24);
        double right = dock != null && Native.GetWindowRect(new WindowInteropHelper(dock).Handle, out var dockRect)
            ? dockRect.Right / scale : area.Right / scale - 12;
        Left = Math.Clamp(right - Width, area.Left / scale + 12, Math.Max(area.Left / scale + 12, area.Right / scale - Width - 12));
        Top = area.Bottom / scale - Height - 10;
    }
    private void Update()
    {
        var app = App.Current;
        syncing = true;
        var monitors = app.Monitors;
        string signature = string.Join('|', monitors.Select(m => m.Id + (m.Brightness.HasValue ? "+" : "-")));
        if (signature != monitorSignature || MonitorChoice.ItemsSource == null)
        {
            MonitorChoice.ItemsSource = monitors;
            MonitorChoice.SelectedValue = app.SelectedMonitor?.Id;
            monitorSignature = signature;
        }
        if (!applying && pending == null)
        {
            var monitor = app.SelectedMonitor;
            BrightnessSlider.IsEnabled = monitor?.Brightness != null;
            BrightnessSlider.Value = monitor?.Brightness ?? 0;
            BrightnessValue.Text = monitor?.Brightness is { } value ? $"{value}%" : "—";
            BrightnessStatus.Text = monitor?.Status ?? "未发现可控制的外接屏。请打开显示器 DDC/CI 后刷新。";
        }
        if (app.Snapshot is { } s)
        {
            NetworkValue.Text = $"{Formats.Rate(s.DownloadBytesPerSecond)} / {Formats.Rate(s.UploadBytesPerSecond)}";
            CpuValue.Text = $"{(s.CpuGhz is { } speed ? $"{speed:0.00} GHz" : "— GHz")} / {(s.CpuUsage is { } usage ? $"{usage:0}%" : "—%")}";
            CpuValue.ToolTip = s.CpuSpeedSource;
            CpuModel.Text = app.CpuName;
            MemoryValue.Text = $"{s.MemoryPercent:0}%";
            FanValue.Text = s.Fans.Count > 0 ? string.Join(" / ", s.Fans.Select(f => $"{f.Rpm:0}")) + " RPM" : "— RPM";
            FanValue.ToolTip = string.Join("\n", s.Fans.Select(f => $"{f.Name}: {f.Rpm:0} RPM"));
            FanStatus.Text = s.FanStatus;
            NetworkValue.ToolTip = s.NetworkName;
            AdminButton.Visibility = s.Fans.Count == 0 && !app.IsAdministrator && s.FanStatus.Contains("管理员") ? Visibility.Visible : Visibility.Collapsed;
        }
        PreviewStrip.Apply(app.Settings, app.Snapshot, app.SelectedMonitor);
        syncing = false;
    }
    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || MonitorChoice.SelectedItem is not MonitorInfo monitor) return;
        pending = null; debounce.Stop();
        App.Current.Settings.SelectedMonitorId = monitor.Id;
        App.Current.SaveSettings(); Update();
    }
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (syncing || BrightnessValue == null || MonitorChoice.SelectedItem is not MonitorInfo { Brightness: not null } monitor) return;
        int value = (int)Math.Round(e.NewValue);
        BrightnessValue.Text = $"{value}%";
        BrightnessStatus.Text = "等待应用…";
        pending = (monitor.Id, value);
        debounce.Stop(); debounce.Start();
    }
    private async Task ApplyPendingAsync()
    {
        if (applying) return;
        applying = true;
        try
        {
            while (pending is { } request)
            {
                pending = null;
                BrightnessStatus.Text = "正在设置硬件亮度…";
                int actual = await App.Current.SetBrightnessAsync(request.Id, request.Value);
                if (App.Current.SelectedMonitor?.Id == request.Id && pending == null)
                {
                    syncing = true; BrightnessSlider.Value = actual; BrightnessValue.Text = $"{actual}%"; syncing = false;
                    BrightnessStatus.Text = actual == request.Value ? "DDC/CI · 已读取确认" : $"显示器返回 {actual}%，与请求值不同。";
                }
            }
        }
        catch (Exception ex) { pending = null; BrightnessStatus.Text = ex.Message; SettingsStore.Log(ex); }
        finally { applying = false; }
    }
    private void Preset(object sender, RoutedEventArgs e)
    {
        if (!BrightnessSlider.IsEnabled) return;
        int value = int.Parse((string)((Button)sender).Tag);
        if (Math.Abs(BrightnessSlider.Value - value) > 0.1) BrightnessSlider.Value = value;
    }
    private async void RefreshMonitors(object sender, RoutedEventArgs e)
    { RefreshButton.IsEnabled = false; try { await App.Current.RefreshMonitorsAsync(); } finally { RefreshButton.IsEnabled = true; } }
    private void NetworkChanged(object sender, SelectionChangedEventArgs e)
    { if (!syncing && NetworkChoice.SelectedItem is AdapterChoice choice) { App.Current.Settings.NetworkInterfaceId = choice.Id; App.Current.SaveSettings(); } }
    private void ModulesChanged(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        var settings = App.Current.Settings;
        settings.ShowDownload = ShowDownload.IsChecked == true; settings.ShowUpload = ShowUpload.IsChecked == true;
        settings.ShowCpuSpeed = ShowCpuSpeed.IsChecked == true; settings.ShowCpuUsage = ShowCpuUsage.IsChecked == true;
        settings.ShowMemory = ShowMemory.IsChecked == true; settings.ShowFan1 = ShowFan1.IsChecked == true;
        settings.ShowFan2 = ShowFan2.IsChecked == true; settings.ShowBrightness = ShowBrightness.IsChecked == true;
        App.Current.SaveSettings();
    }
    private void OffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!syncing && IsLoaded) { App.Current.Settings.HorizontalOffset = (int)e.NewValue; App.Current.SaveSettings(); } }
    private async void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        if (sender == StartupElevated && Startup.IsChecked != true) { App.Current.Settings.AutoStartElevated = StartupElevated.IsChecked == true; App.Current.SaveSettings(); return; }
        Startup.IsEnabled = StartupElevated.IsEnabled = false;
        try
        {
            App.Current.Settings.AutoStartElevated = StartupElevated.IsChecked == true;
            await StartupService.SetEnabledAsync(Startup.IsChecked == true, StartupElevated.IsChecked == true);
            App.Current.SaveSettings();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "无法更改启动设置"); }
        finally { Startup.IsChecked = StartupService.Enabled; StartupStatus.Text = StartupService.Description; Startup.IsEnabled = StartupElevated.IsEnabled = true; }
    }
    private void VisualOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        App.Current.Settings.ShowCpuValues = CpuValues.IsChecked == true;
        App.Current.Settings.ShowFanRpm = FanRpm.IsChecked == true;
        App.Current.SaveSettings();
    }
    private void FanScaleChanged(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        if (int.TryParse(FanScale.Text, out int rpm) && rpm is >= 1000 and <= 20000) App.Current.Settings.FanScaleRpm = rpm;
        FanScale.Text = App.Current.Settings.FanScaleRpm.ToString();
        App.Current.SaveSettings();
    }
    private void AddAction(object sender, RoutedEventArgs e)
    {
        var dialog = new ActionDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        { App.Current.Settings.Actions.Add(dialog.Result); App.Current.SaveSettings(); RenderActions(); }
    }
    private void RenderActions()
    {
        ActionList.Children.Clear();
        ActionsEmpty.Visibility = App.Current.Settings.Actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var action in App.Current.Settings.Actions)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var remove = new Button { Content = "×", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(5, 0, 0, 0), ToolTip = "移除此快捷操作" };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += (_, _) => { App.Current.Settings.Actions.Remove(action); App.Current.SaveSettings(); RenderActions(); };
            var run = new Button { Content = action.Name, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = action.Target };
            run.Click += (_, _) => { try { QuickActions.Run(action); } catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "无法打开快捷操作"); } };
            row.Children.Add(remove); row.Children.Add(run); ActionList.Children.Add(row);
        }
    }
    private void ClosePanel(object sender, RoutedEventArgs e) => Hide();
    private void OpenDisplayOptions(object sender, RoutedEventArgs e) { DisplayOptions.IsExpanded = true; DisplayOptions.BringIntoView(); }
    private void PanelKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); }
    private void Quit(object sender, RoutedEventArgs e) => App.Current.RequestShutdown();
    private void RestartAdmin(object sender, RoutedEventArgs e) => App.Current.RestartElevated();
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); }
    private sealed record AdapterChoice(string? Id, string Name);
}
