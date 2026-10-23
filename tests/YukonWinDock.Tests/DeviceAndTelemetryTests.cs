using YukonWinDock.Core;
using YukonWinDock.Services;

namespace YukonWinDock.Tests;

public class DeviceAndTelemetryTests
{
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
