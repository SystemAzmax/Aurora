using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Tests.Fakes;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.Tests.UI;

public class ApplicationHostedServiceTests
{
    private readonly IWallpaperScheduler _scheduler = Substitute.For<IWallpaperScheduler>();
    private readonly ITrayIconService _tray = Substitute.For<ITrayIconService>();
    private readonly ISettingsWindowService _settingsWindow = Substitute.For<ISettingsWindowService>();
    private readonly IStartupRegistrationService _startup = Substitute.For<IStartupRegistrationService>();
    private readonly IMonitorConfigurationWatcher _watcher = Substitute.For<IMonitorConfigurationWatcher>();

    [Fact]
    public async Task フォルダが1つも無ければ起動時に設定画面を開く()
    {
        await StartAsync(new AppSettings().WithMonitor(MonitorSettings.CreateDefault("M")));

        _settingsWindow.Received(1).Show();
        _tray.Received(1).Show();
        _scheduler.Received(1).Start();
        await _watcher.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 一枚表示のフォルダがあれば設定画面を開かない()
    {
        await StartAsync(new AppSettings().WithMonitor(MonitorSettings.CreateDefault("M").AddFolder(@"C:\w")));

        _settingsWindow.DidNotReceive().Show();
    }

    [Fact]
    public async Task 四分割でマスだけにフォルダがあれば設定画面を開かない()
    {
        await StartAsync(new AppSettings().WithMonitor(
            MonitorSettings.CreateDefault("M").AddTileFolder(1, @"C:\tile") with { Layout = WallpaperLayout.Grid2x2 }));

        _settingsWindow.DidNotReceive().Show();
    }

    [Fact]
    public async Task 起動時に自動起動の登録パスを更新する()
    {
        await StartAsync(new AppSettings());

        _startup.Received(1).UpdateRegisteredPathIfNeeded();
    }

    [Fact]
    public async Task 自動起動の登録パスを更新できなくても起動を続ける()
    {
        _startup.UpdateRegisteredPathIfNeeded().Returns(_ => throw new UnauthorizedAccessException());

        await StartAsync(new AppSettings());

        _tray.Received(1).Show();
        _scheduler.Received(1).Start();
    }

    [Fact]
    public async Task 設定ファイルを読めず既定値で起動した場合は理由を通知する()
    {
        var settings = new InMemorySettingsService { LoadWarning = "設定ファイルを読み込めなかったため" };

        await Create(settings).StartAsync(TestContext.Current.CancellationToken);

        _tray.Received(1).ShowNotification(Arg.Any<string>(), "設定ファイルを読み込めなかったため");
        _scheduler.Received(1).Start();
    }

    [Fact]
    public async Task 設定ファイルに問題が無ければ通知しない()
    {
        await StartAsync(new AppSettings());

        _tray.DidNotReceive().ShowNotification(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task 終了処理は何度呼ばれても1回だけ実行する()
    {
        ApplicationHostedService sut = Create(new AppSettings());
        CancellationToken ct = TestContext.Current.CancellationToken;

        await Task.WhenAll(sut.StopAsync(ct), sut.StopAsync(ct));
        await sut.StopAsync(ct);

        await _watcher.Received(1).StopAsync();
        await _scheduler.Received(1).StopAsync();
        _tray.Received(1).Dispose();
        _settingsWindow.Received(1).Close();
    }

    private Task StartAsync(AppSettings settings) => Create(settings).StartAsync(TestContext.Current.CancellationToken);

    private ApplicationHostedService Create(AppSettings settings) => Create(new InMemorySettingsService(settings));

    private ApplicationHostedService Create(InMemorySettingsService settings) => new(
        settings,
        _scheduler,
        _tray,
        _settingsWindow,
        _startup,
        _watcher,
        new InlineUiDispatcher(),
        NullLogger<ApplicationHostedService>.Instance);
}
