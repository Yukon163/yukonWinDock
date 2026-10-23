using YukonWinDock.Core;
using YukonWinDock.Services;

namespace YukonWinDock.Tests;

public class DeviceAndTelemetryTests
{
    [Fact]
    public void MissingStartupTaskIsDisabledForBothComAndManagedExceptions()
    {
        Assert.False(StartupService.QueryScheduled(() => throw new FileNotFoundException()));
        Assert.False(StartupService.QueryScheduled(() => throw new System.Runtime.InteropServices.COMException("task missing", unchecked((int)0x80070002))));
        Assert.True(StartupService.QueryScheduled(() => true));
        Assert.Throws<UnauthorizedAccessException>(() => StartupService.QueryScheduled(() => throw new UnauthorizedAccessException()));
    }
    [Fact]
    public void RealStartupStatusCanBeReadBeforeAnyTaskIsRegistered()
    {
        bool scheduled = StartupService.Scheduled;
        bool enabled = StartupService.Enabled;
        Assert.False(scheduled && !enabled);
        Assert.False(string.IsNullOrWhiteSpace(StartupService.Description));
    }
    [Fact]
    public void VendoredIconsHaveValidWpfVectorGeometry()
    {
        using var packed = typeof(DockLayout).Assembly.GetManifestResourceStream("yukonWinDock.g.resources");
        Assert.NotNull(packed);
        using var reader = new System.Resources.ResourceReader(packed);
        int count = 0;
        foreach (System.Collections.DictionaryEntry item in reader)
        {
            if (!((string)item.Key).EndsWith(".svg", StringComparison.Ordinal)) continue;
            using var stream = (Stream)item.Value!;
            var root = System.Xml.Linq.XDocument.Load(stream).Root!;
            foreach (var path in root.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                var geometry = System.Windows.Media.Geometry.Parse(path.Attribute("d")!.Value);
                Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0);
            }
            count++;
        }
        Assert.Equal(4, count);
    }
    [Fact]
    public void CompactLayoutAndOptionalValuesDoNotOverlap()
    {
        var compact = new AppSettings();
        Assert.True(DockLayout.Width(compact) < 350);
        compact.ShowFanRpm = true;
        var tiles = DockLayout.Tiles(compact);
        for (int i = 1; i < tiles.Count; i++) Assert.True(tiles[i].X >= tiles[i - 1].X + tiles[i - 1].Width + 8);
        double withValues = DockLayout.Width(compact);
        compact.ShowCpuValues = false;
        Assert.True(DockLayout.Width(compact) < withValues);
    }
    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.0, 0)]
    [InlineData(750.0, 1)]
    [InlineData(2300.0, 4)]
    [InlineData(6000.0, 8)]
    [InlineData(9000.0, 8)]
    public void FanMeterFillsBottomSegmentsAndClampsToReference(double? rpm, int blocks)
        => Assert.Equal(blocks, DockLayout.FanBlocks(rpm, 6000));
    [Fact]
    public void CpuColorHasExplicitUnknownAndLoadThresholds()
    {
        Assert.Equal("#88939F", DockLayout.LoadColor(null));
        Assert.Equal("#79D5B1", DockLayout.LoadColor(54.9));
        Assert.Equal("#E4BB6C", DockLayout.LoadColor(55));
        Assert.Equal("#F27D82", DockLayout.LoadColor(85));
    }
    [Fact]
    public void VisualOptionsSurviveReloadAndInvalidScalesAreClamped()
    {
        var restored = SettingsStore.Decode("{\"SettingsVersion\":2,\"ShowFanRpm\":true,\"ShowCpuValues\":false,\"FanScaleRpm\":0}");
        Assert.True(restored.ShowFanRpm); Assert.False(restored.ShowCpuValues);
        Assert.Equal(1000, restored.FanScaleRpm);
        Assert.Equal(6000, SettingsStore.Decode("null").FanScaleRpm);
        Assert.Equal("\"C:\\My Apps\\yukonWinDock.exe\"", StartupService.CommandFor(@"C:\My Apps\yukonWinDock.exe"));
    }
    [Fact]
    public void SecondClickClosesAndTrayDeactivationDoesNotReopen()
    {
        Assert.False(PanelTogglePolicy.ShouldShow(true, TimeSpan.MaxValue));
        Assert.False(PanelTogglePolicy.ShouldShow(false, TimeSpan.FromMilliseconds(30)));
        Assert.True(PanelTogglePolicy.ShouldShow(false, TimeSpan.FromSeconds(1)));
    }
    [Fact]
    public void AllMetricsCanBeDisabledAndRemainDisabledAfterRestart()
    {
        var settings = new AppSettings { ShowDownload = false, ShowUpload = false, ShowCpuSpeed = false,
            ShowCpuUsage = false, ShowMemory = false, ShowFan1 = false, ShowFan2 = false, ShowBrightness = false };
        var restored = SettingsStore.Decode(System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.False(restored.ShowDownload || restored.ShowUpload || restored.ShowCpuSpeed || restored.ShowCpuUsage ||
            restored.ShowMemory || restored.ShowFan1 || restored.ShowFan2 || restored.ShowBrightness);
    }
    [Fact]
    public void LegacySettingsMigrateWithoutRestoringHiddenGroups()
    {
        var restored = SettingsStore.Decode("{\"ShowNetwork\":false,\"ShowCpu\":true,\"ShowFans\":false,\"ShowBrightness\":false}");
        Assert.False(restored.ShowDownload || restored.ShowUpload || restored.ShowFan1 || restored.ShowFan2 || restored.ShowBrightness);
        Assert.True(restored.ShowCpuSpeed && restored.ShowCpuUsage);
        Assert.Equal(2, restored.SettingsVersion);
    }
    [Fact]
    public async Task SupportedDisplayAcceptsItsCurrentBrightnessAndConfirmsReadback()
    {
        // An idempotent hardware write exercises DDC without changing the user's brightness.
        var service = new BrightnessService();
        var monitor = (await service.ReadAsync()).FirstOrDefault(m => m.Brightness.HasValue);
        if (monitor == null) return; // Headless Windows CI has no physical displays.
        int expected = monitor.Brightness!.Value;
        Assert.Equal(expected, await service.SetAsync(monitor.Id, expected));
        Assert.Equal(expected, (await service.ReadAsync()).Single(m => m.Id == monitor.Id).Brightness);
    }
    [Fact]
    public void CounterResetAndReconnectDoNotProduceNegativeTraffic()
    {
        Assert.Equal(0, TelemetryService.RateDelta(90000, 20, 1));
        Assert.Equal(0, TelemetryService.RateDelta(20, 90000, 0));
        Assert.Equal(2048, TelemetryService.RateDelta(300, 4396, 2));
    }
    [Fact]
    public void NonZeroHardwareRangeIsScaledAndClamped()
    {
        Assert.Equal(52u, Formats.ToNative(40, 100, 20));
        Assert.Equal(20, Formats.ToPercent(40, 52, 100));
        Assert.Equal(100u, Formats.ToNative(40, 100, 500));
        Assert.Equal(0, Formats.ToPercent(40, 10, 100));
        Assert.Equal(0, Formats.ToPercent(40, 40, 40));
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("powershell.exe -Command anything")]
    [InlineData("file:///C:/Windows")]
    [InlineData("ms-settings:privacy")]
    public void QuickActionsRejectCommandsAndNonWebSchemes(string target) => Assert.NotNull(QuickActions.Validate(target));

    [Fact]
    public void QuickActionsAcceptExistingPathsAndHttps()
    {
        Assert.Null(QuickActions.Validate(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.Null(QuickActions.Validate("https://github.com/Yukon163/yukonWinDock"));
    }
    [Fact]
    public async Task RealWindowsTelemetryHasNoFirstSampleSpikeAndUpdatesCpu()
    {
        using var service = new TelemetryService();
        var first = service.Sample();
        Assert.Equal(0, first.DownloadBytesPerSecond);
        Assert.Equal(0, first.UploadBytesPerSecond);
        Assert.Null(first.CpuUsage);
        await Task.Delay(1100);
        var next = service.Sample();
        Assert.NotNull(next.CpuUsage);
        Assert.InRange(next.CpuUsage.Value, 0, 100);
        Assert.True(next.DownloadBytesPerSecond >= 0);
        Assert.InRange(next.MemoryPercent, 0, 100);
    }
    [Fact]
    public async Task BrightnessReadsCanRunConcurrentlyAndRejectInvalidWrites()
    {
        var service = new BrightnessService();
        var reads = await Task.WhenAll(service.ReadAsync(), service.ReadAsync());
        Assert.Equal(reads[0].Select(m => m.Id), reads[1].Select(m => m.Id));
        Assert.All(reads[0].Where(m => m.Brightness.HasValue), m => Assert.InRange(m.Brightness!.Value, 0, 100));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetAsync("nonexistent", 101));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync("nonexistent", 50));
    }
}
