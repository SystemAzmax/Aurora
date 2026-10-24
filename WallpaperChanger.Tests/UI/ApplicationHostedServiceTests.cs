using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
    private readonly IActivationRequestListener _activation = Substitute.For<IActivationRequestListener>();
    private readonly FakeTimeProvider _time = new();

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

    [Fact]
    public async Task 起動後に二つ目の起動の要求を受けると設定画面を前面に出す()
    {
        Action? onRequested = null;
        _activation.StartListening(Arg.Do<Action>(a => onRequested = a));

        await StartAsync(new AppSettings().WithMonitor(MonitorSettings.CreateDefault("M").AddFolder(@"C:\w")));
        Assert.NotNull(onRequested);
        onRequested();

        _settingsWindow.Received(1).Show();
    }

    [Fact]
    public async Task 終了処理では要求の受け付けを止め終了後の要求では設定画面を開かない()
    {
        Action? onRequested = null;
        _activation.StartListening(Arg.Do<Action>(a => onRequested = a));
        ApplicationHostedService sut = Create(new AppSettings().WithMonitor(MonitorSettings.CreateDefault("M").AddFolder(@"C:\w")));
        CancellationToken ct = TestContext.Current.CancellationToken;
        await sut.StartAsync(ct);

        await sut.StopAsync(ct);
        Assert.NotNull(onRequested);
        onRequested();

        _activation.Received(1).StopListening();
        _settingsWindow.DidNotReceive().Show();
    }

    [Fact]
    public async Task モニター構成の監視を始められなくても起動を続け始められるまで再試行する()
    {
        var timeout = new TimeoutException("エクスプローラーが応答しません");
        _watcher.StartAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(timeout), Task.FromException(timeout), Task.CompletedTask);
        ApplicationHostedService sut = Create(new AppSettings());

        await sut.StartAsync(TestContext.Current.CancellationToken);

        // 監視を始められなくても、起動処理は最後まで進める
        _activation.Received(1).StartListening(Arg.Any<Action>());
        Assert.Equal(1, WatcherStartCount());

        _time.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => WatcherStartCount() == 2);
        _time.Advance(TimeSpan.FromSeconds(20));
        await sut.MonitorWatcherStart.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(3, WatcherStartCount());
    }

    [Fact]
    public async Task 再試行の待ち時間は倍にしていき上限で止める()
    {
        _watcher.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new TimeoutException()));
        ApplicationHostedService sut = Create(new AppSettings());
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // 10 秒、20 秒、40 秒、80 秒、160 秒と倍にしていき、その後は上限（5 分）の間隔で再試行する
        int[] delaySeconds = [10, 20, 40, 80, 160, 300, 300];
        for (int i = 0; i < delaySeconds.Length; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(delaySeconds[i] - 1));
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(i + 1, WatcherStartCount());

            _time.Advance(TimeSpan.FromSeconds(1));
            int expected = i + 2;
            await WaitUntilAsync(() => WatcherStartCount() == expected);
        }

        await sut.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 再試行中に終了したら再試行をやめる()
    {
        _watcher.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new TimeoutException()));
        ApplicationHostedService sut = Create(new AppSettings());
        await sut.StartAsync(TestContext.Current.CancellationToken);

        await sut.StopAsync(TestContext.Current.CancellationToken);
        _time.Advance(ApplicationHostedService.MaxRetryDelay);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, WatcherStartCount());
        Assert.True(sut.MonitorWatcherStart.IsCompleted);
        await _watcher.Received(1).StopAsync();
    }

    [Fact]
    public async Task モニターの取得が終わらなくても起動処理を進め終了も待たせない()
    {
        // エクスプローラーが応答せず、モニターの取得が時間切れになるまで終わらない状態
        var hung = new TaskCompletionSource();
        _watcher.StartAsync(Arg.Any<CancellationToken>()).Returns(hung.Task);
        ApplicationHostedService sut = Create(new AppSettings());

        await sut.StartAsync(TestContext.Current.CancellationToken);

        _settingsWindow.Received(1).Show();
        _activation.Received(1).StartListening(Arg.Any<Action>());
        await sut.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await _watcher.Received(1).StopAsync();
    }

    private int WatcherStartCount() =>
        _watcher.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IMonitorConfigurationWatcher.StartAsync));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
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
        _activation,
        new InlineUiDispatcher(),
        _time,
        NullLogger<ApplicationHostedService>.Instance);
}
