using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure.DesktopWallpaper;

namespace WallpaperChanger.Tests.Integration;

/// <summary>
/// 実機の IDesktopWallpaper を呼び出す統合テスト。壁紙は変更しない（読み取りのみ）。
/// 対話セッションの無い CI では除外すること: dotnet test -- --filter-not-trait "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesktopWallpaperIntegrationTests : IDisposable
{
    private readonly DesktopWallpaperComHost _comHost = new(
        Options.Create(new DesktopWallpaperOptions()), NullLogger<DesktopWallpaperComHost>.Instance);

    public void Dispose() => _comHost.Dispose();

    [Fact]
    public async Task 接続中のモニターを名前と解像度付きで取得できる()
    {
        var sut = new DesktopWallpaperMonitorService(
            _comHost,
            new DisplayConfigDeviceInfoProvider(NullLogger<DisplayConfigDeviceInfoProvider>.Instance),
            NullLogger<DesktopWallpaperMonitorService>.Instance);

        IReadOnlyList<MonitorInfo> monitors = await sut.GetMonitorsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.False(string.IsNullOrWhiteSpace(m.DisplayName));
            Assert.True(m.Width > 0 && m.Height > 0);
        });
        Assert.Single(monitors, m => m.IsPrimary);

        foreach (MonitorInfo m in monitors)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"[{m.Index}] {m.DisplayName} {m.ResolutionText} Bounds={m.Bounds} Primary={m.IsPrimary} Id={m.Id}");
        }
    }

    [Fact]
    public async Task 現在の壁紙を取得できる()
    {
        var monitorService = new DesktopWallpaperMonitorService(
            _comHost,
            new DisplayConfigDeviceInfoProvider(NullLogger<DisplayConfigDeviceInfoProvider>.Instance),
            NullLogger<DesktopWallpaperMonitorService>.Instance);
        var sut = new DesktopWallpaperService(_comHost);
        CancellationToken ct = TestContext.Current.CancellationToken;

        foreach (MonitorInfo monitor in await monitorService.GetMonitorsAsync(ct))
        {
            string? path = await sut.GetWallpaperAsync(monitor.Id, ct);
            TestContext.Current.TestOutputHelper?.WriteLine($"{monitor.DisplayName}: {path ?? "(単色)"}");
        }
    }

    [Fact]
    public async Task 存在しないファイルは設定前に例外になる()
    {
        var sut = new DesktopWallpaperService(_comHost);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.SetWallpaperAsync("dummy", @"C:\__not_exists__\x.jpg", TestContext.Current.CancellationToken));
    }
}
