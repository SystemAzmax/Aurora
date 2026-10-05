using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WallpaperChanger.Core.Interfaces;
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
    private readonly TrayIconViewModel _sut;

    public TrayIconViewModelTests()
    {
        _scheduler.Interval.Returns(TimeSpan.FromMinutes(90));
        _sut = new TrayIconViewModel(
            _rotation, _scheduler, _settingsWindow, _lifetime, new InlineUiDispatcher(), NullLogger<TrayIconViewModel>.Instance);
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
}
