using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Tests.Fakes;
using WallpaperChanger.UI.Services;
using WallpaperChanger.UI.ViewModels;

namespace WallpaperChanger.Tests.UI;

public sealed class TrayIconViewModelTests : IDisposable
{
    private readonly IWallpaperRotationService _rotation = Substitute.For<IWallpaperRotationService>();
    private readonly IWallpaperScheduler _scheduler = Substitute.For<IWallpaperScheduler>();
    private readonly ISettingsWindowService _settingsWindow = Substitute.For<ISettingsWindowService>();
    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
    private readonly FakeTimeProvider _time = new();
    private readonly TrayIconViewModel _sut;

    public TrayIconViewModelTests()
    {
        _scheduler.Interval.Returns(TimeSpan.FromMinutes(90));
        _scheduler.ChangeNowAsync(Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.Changed));
        _scheduler.RefreshNowAsync(Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.Changed));
        _rotation.PreviousAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.Changed));
        _sut = new TrayIconViewModel(
            _rotation, _scheduler, _settingsWindow, _lifetime, new InlineUiDispatcher(), _time, NullLogger<TrayIconViewModel>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task 次の壁紙は即時変更を実行する()
    {
        await _sut.NextWallpaperCommand.ExecuteAsync(null);

        await _scheduler.Received(1).ChangeNowAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 前の壁紙は全モニターを履歴で戻す()
    {
        await _sut.PreviousWallpaperCommand.ExecuteAsync(null);

        await _rotation.Received(1).PreviousAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 全壁紙を更新はフォルダを読み直して選び直す()
    {
        await _sut.RefreshAllWallpapersCommand.ExecuteAsync(null);

        await _scheduler.Received(1).RefreshNowAsync(Arg.Any<CancellationToken>());
        await _scheduler.DidNotReceive().ChangeNowAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 一時停止と再開を切り替える()
    {
        await _sut.TogglePauseCommand.ExecuteAsync(null);
        await _scheduler.Received(1).PauseAsync(Arg.Any<CancellationToken>());

        _scheduler.IsPaused.Returns(true);
        _scheduler.StateChanged += Raise.Event();

        Assert.True(_sut.IsPaused);
        Assert.Equal("再開", _sut.PauseMenuHeader);
        Assert.Contains("一時停止中", _sut.ToolTipText, StringComparison.Ordinal);

        await _sut.TogglePauseCommand.ExecuteAsync(null);
        await _scheduler.Received(1).ResumeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ツールチップに切り替え間隔を表示する()
    {
        Assert.Equal("壁紙チェンジャー（1 時間 30 分ごとに切り替え）", _sut.ToolTipText);
    }

    [Fact]
    public void 設定と終了()
    {
        _sut.ShowSettingsCommand.Execute(null);
        _sut.ExitCommand.Execute(null);

        _settingsWindow.Received(1).Show();
        _lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task 切り替えできれば通知しない()
    {
        TrayNotificationEventArgs? notification = null;
        _sut.NotificationRequested += (_, e) => notification = e;

        await _sut.NextWallpaperCommand.ExecuteAsync(null);
        await _sut.PreviousWallpaperCommand.ExecuteAsync(null);
        await _sut.RefreshAllWallpapersCommand.ExecuteAsync(null);

        Assert.Null(notification);
    }

    [Fact]
    public async Task 全壁紙を更新で何も変わらなければ理由を通知する()
    {
        _scheduler.RefreshNowAsync(Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.NoFolders));
        TrayNotificationEventArgs? notification = null;
        _sut.NotificationRequested += (_, e) => notification = e;

        await _sut.RefreshAllWallpapersCommand.ExecuteAsync(null);

        Assert.NotNull(notification);
        Assert.Contains("壁紙フォルダが登録されていません", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 次の壁紙で何も変わらなければ理由を通知する()
    {
        _scheduler.ChangeNowAsync(Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.NoImages));
        TrayNotificationEventArgs? notification = null;
        _sut.NotificationRequested += (_, e) => notification = e;

        await _sut.NextWallpaperCommand.ExecuteAsync(null);

        Assert.NotNull(notification);
        Assert.False(notification.IsError);
        Assert.Contains("対応する画像がありません（Monitor）", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 前の壁紙が無ければ理由を通知する()
    {
        _rotation.PreviousAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.NoPreviousWallpaper));
        TrayNotificationEventArgs? notification = null;
        _sut.NotificationRequested += (_, e) => notification = e;

        await _sut.PreviousWallpaperCommand.ExecuteAsync(null);

        Assert.NotNull(notification);
        Assert.Contains("これ以上前の壁紙はありません", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 失敗時はエラー通知を要求する()
    {
        _scheduler.ChangeNowAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("壊れた画像"));
        TrayNotificationEventArgs? notification = null;
        _sut.NotificationRequested += (_, e) => notification = e;

        await _sut.NextWallpaperCommand.ExecuteAsync(null);

        Assert.NotNull(notification);
        Assert.True(notification.IsError);
        Assert.Contains("壊れた画像", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 切り替え中は進み具合をツールチップに表示しメニューから切り替えられない()
    {
        _rotation.ProgressChanged += Raise.EventWith(new WallpaperChangeProgressEventArgs(2, 4, Monitor("Beyond TV")));

        Assert.True(_sut.IsChangingWallpaper);
        Assert.Equal("壁紙チェンジャー\n壁紙を切り替えています…（4 台中 3 台目: Beyond TV）", _sut.ToolTipText);
        Assert.False(_sut.NextWallpaperCommand.CanExecute(null));
        Assert.False(_sut.PreviousWallpaperCommand.CanExecute(null));
        Assert.False(_sut.RefreshAllWallpapersCommand.CanExecute(null));

        _rotation.ProgressChanged += Raise.EventWith(new WallpaperChangeProgressEventArgs(4, 4, null));

        Assert.False(_sut.IsChangingWallpaper);
        Assert.Equal("壁紙チェンジャー（1 時間 30 分ごとに切り替え）", _sut.ToolTipText);
        Assert.True(_sut.NextWallpaperCommand.CanExecute(null));
    }

    [Fact]
    public async Task メニューから切り替えると終わるまで進み具合を表示し終わったことを少し表示してから閉じる()
    {
        var change = new TaskCompletionSource<WallpaperChangeResult>();
        _scheduler.RefreshNowAsync(Arg.Any<CancellationToken>()).Returns(change.Task);

        Task running = _sut.RefreshAllWallpapersCommand.ExecuteAsync(null);

        Assert.True(_sut.IsProgressPopupOpen);
        Assert.Equal("壁紙を切り替えています…", _sut.ProgressPopupText);
        Assert.False(_sut.NextWallpaperCommand.CanExecute(null));
        _rotation.ProgressChanged += Raise.EventWith(new WallpaperChangeProgressEventArgs(0, 2, Monitor("Left")));
        Assert.Equal("壁紙を切り替えています…（2 台中 1 台目: Left）", _sut.ProgressPopupText);

        _rotation.ProgressChanged += Raise.EventWith(new WallpaperChangeProgressEventArgs(2, 2, null));
        change.SetResult(Result(WallpaperChangeStatus.Changed));
        await running;

        Assert.True(_sut.IsProgressPopupOpen);
        Assert.Equal("壁紙を切り替えました", _sut.ProgressPopupText);
        Assert.True(_sut.NextWallpaperCommand.CanExecute(null));

        _time.Advance(TrayIconViewModel.CompletedPopupDuration);
        await WaitUntilAsync(() => !_sut.IsProgressPopupOpen);
    }

    [Fact]
    public async Task 変わらなかったモニターがあれば進み具合の表示をすぐに閉じる()
    {
        _scheduler.ChangeNowAsync(Arg.Any<CancellationToken>()).Returns(Result(WallpaperChangeStatus.NoImages));

        await _sut.NextWallpaperCommand.ExecuteAsync(null);

        Assert.False(_sut.IsProgressPopupOpen);
    }

    [Fact]
    public async Task 失敗したら進み具合の表示をすぐに閉じる()
    {
        _rotation.PreviousAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await _sut.PreviousWallpaperCommand.ExecuteAsync(null);

        Assert.False(_sut.IsProgressPopupOpen);
        Assert.False(_sut.IsMenuChangeRunning);
    }

    [Fact]
    public async Task 終わったことの表示中に次の切り替えを始めると前の切り替えの予約では閉じない()
    {
        await _sut.NextWallpaperCommand.ExecuteAsync(null);
        var change = new TaskCompletionSource<WallpaperChangeResult>();
        _scheduler.RefreshNowAsync(Arg.Any<CancellationToken>()).Returns(change.Task);
        Task running = _sut.RefreshAllWallpapersCommand.ExecuteAsync(null);

        _time.Advance(TrayIconViewModel.CompletedPopupDuration);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(_sut.IsProgressPopupOpen);
        change.SetResult(Result(WallpaperChangeStatus.Changed));
        await running;
        _time.Advance(TrayIconViewModel.CompletedPopupDuration);
        await WaitUntilAsync(() => !_sut.IsProgressPopupOpen);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException();
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static MonitorInfo Monitor(string name) =>
        new("MON", 0, name, new MonitorBounds(0, 0, 1920, 1080), 1920, 1080);

    private static WallpaperChangeResult Result(WallpaperChangeStatus status) =>
        new([new MonitorChangeResult(new MonitorInfo("MON", 0, "Monitor", new MonitorBounds(0, 0, 1920, 1080), 1920, 1080), status)]);
}
