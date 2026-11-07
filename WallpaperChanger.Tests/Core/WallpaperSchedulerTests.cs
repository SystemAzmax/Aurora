using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Core;

public sealed class WallpaperSchedulerTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly IWallpaperRotationService _rotation = Substitute.For<IWallpaperRotationService>();
    private readonly ISystemStateProvider _systemState = Substitute.For<ISystemStateProvider>();
    private readonly InMemorySettingsService _settings = new(new AppSettings { IntervalMinutes = 10 });
    private readonly WallpaperScheduler _sut;
    private int _nextCount;
    private TaskCompletionSource _changed = NewSignal();

    public WallpaperSchedulerTests()
    {
        _rotation.NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref _nextCount);
            _changed.TrySetResult();
            return Task.FromResult(WallpaperChangeResult.Empty);
        });

        _sut = new WallpaperScheduler(_rotation, _settings, _systemState, _time, NullLogger<WallpaperScheduler>.Instance);
    }

    public ValueTask DisposeAsync() => _sut.DisposeAsync();

    [Fact]
    public async Task 間隔が経過すると壁紙を変更する()
    {
        _sut.Start();
        await WaitForTimerAsync();

        _time.Advance(TimeSpan.FromMinutes(10));

        await _changed.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, _nextCount);
    }

    [Fact]
    public async Task 間隔前には変更しない()
    {
        _sut.Start();
        await WaitForTimerAsync();

        _time.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, _nextCount);
    }

    [Fact]
    public async Task 一時停止中は変更せず状態が設定に保存される()
    {
        _sut.Start();
        await WaitForTimerAsync();

        await _sut.PauseAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(30));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(_sut.IsPaused);
        Assert.True(_settings.Current.IsPaused);
        Assert.Equal(0, _nextCount);
    }

    [Fact]
    public async Task 再開後は間隔経過で変更する()
    {
        await _settings.UpdateAsync(s => s with { IsPaused = true }, TestContext.Current.CancellationToken);
        _sut.Start();

        await _sut.ResumeAsync(TestContext.Current.CancellationToken);
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        await _changed.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
        Assert.False(_sut.IsPaused);
    }

    [Fact]
    public async Task 設定の間隔変更に追従する()
    {
        _sut.Start();
        await WaitForTimerAsync();

        await _settings.UpdateAsync(s => s.WithInterval(1), TestContext.Current.CancellationToken);
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(1));

        await _changed.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromMinutes(1), _sut.Interval);
    }

    [Fact]
    public async Task 即時変更はすぐに壁紙を変更する()
    {
        _sut.Start();

        await _sut.ChangeNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _nextCount);
    }

    [Fact]
    public async Task 即時変更でタイマーを何度リセットしても定期実行は続く()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _sut.Start();
        await WaitForTimerAsync();
        for (int i = 0; i < 5; i++)
        {
            await _sut.ChangeNowAsync(ct);
        }

        _changed = NewSignal();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        await _changed.Task.WaitAsync(WaitTimeout, ct);
        Assert.Equal(6, _nextCount);
    }

    [Fact]
    public async Task 全壁紙の更新はフォルダを読み直して変更し待ち時間をリセットする()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _rotation.RefreshAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(WallpaperChangeResult.Empty);
        _sut.Start();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(9));

        await _sut.RefreshNowAsync(ct);
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(100, ct);

        await _rotation.Received(1).RefreshAsync(null, Arg.Any<CancellationToken>());
        Assert.Equal(0, _nextCount);

        _time.Advance(TimeSpan.FromMinutes(1));
        await _changed.Task.WaitAsync(WaitTimeout, ct);
        Assert.Equal(1, _nextCount);
    }

    [Fact]
    public async Task 変更が失敗しても定期実行は継続する()
    {
        int calls = 0;
        _rotation.NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return Task.FromException<WallpaperChangeResult>(new InvalidOperationException("boom"));
            }

            _changed.TrySetResult();
            return Task.FromResult(WallpaperChangeResult.Empty);
        });

        _sut.Start();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        await _changed.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    // ---- 自動切り替えを見合わせる条件 ----

    [Fact]
    public async Task 全画面アプリの実行中は切り替えを待ち終わったら切り替える()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _systemState.IsFullScreenAppRunning().Returns(true);
        int stateChanges = 0;
        _sut.StateChanged += (_, _) => Interlocked.Increment(ref stateChanges);
        _sut.Start();
        await WaitForTimerAsync();

        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.FullScreen);
        await WaitForTimerAsync();
        _time.Advance(WallpaperScheduler.HoldCheckInterval);
        await Task.Delay(100, ct);
        Assert.Equal(0, _nextCount);
        Assert.Equal(AutoChangeHold.FullScreen, _sut.Hold);

        _systemState.IsFullScreenAppRunning().Returns(false);
        await WaitForTimerAsync();
        _time.Advance(WallpaperScheduler.HoldCheckInterval);

        await _changed.Task.WaitAsync(WaitTimeout, ct);
        Assert.Equal(1, _nextCount);
        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.None);
        Assert.Equal(3, Volatile.Read(ref stateChanges)); // 開始、待ち始め、待ち終わり
    }

    [Fact]
    public async Task バッテリー駆動中は切り替えを待つ()
    {
        _systemState.IsOnBatteryPower().Returns(true);
        _sut.Start();
        await WaitForTimerAsync();

        _time.Advance(TimeSpan.FromMinutes(10));

        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.OnBattery);
        Assert.Equal(0, _nextCount);
    }

    [Fact]
    public async Task 条件を無効にしていれば全画面アプリの実行中やバッテリー駆動中も切り替える()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _systemState.IsFullScreenAppRunning().Returns(true);
        _systemState.IsOnBatteryPower().Returns(true);
        await _settings.UpdateAsync(s => s with { SkipConditions = new SkipConditions { WhenFullScreen = false, OnBattery = false } }, ct);
        _sut.Start();
        await WaitForTimerAsync();

        _time.Advance(TimeSpan.FromMinutes(10));

        await _changed.Task.WaitAsync(WaitTimeout, ct);
        Assert.Equal(AutoChangeHold.None, _sut.Hold);
    }

    [Fact]
    public async Task 待った後の切り替えから1間隔待つ()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _systemState.IsFullScreenAppRunning().Returns(true);
        _sut.Start();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.FullScreen);

        // 次の切り替えの時刻（20 分）を過ぎるまで待たせる
        for (int i = 0; i < 12; i++)
        {
            await WaitForTimerAsync();
            _time.Advance(WallpaperScheduler.HoldCheckInterval);
        }

        _systemState.IsFullScreenAppRunning().Returns(false);
        await WaitForTimerAsync();
        _time.Advance(WallpaperScheduler.HoldCheckInterval);
        await _changed.Task.WaitAsync(WaitTimeout, ct);

        // 過ぎた時刻の分を続けて切り替えない
        _changed = NewSignal();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(100, ct);
        Assert.Equal(1, _nextCount);

        _time.Advance(TimeSpan.FromMinutes(1));
        await _changed.Task.WaitAsync(WaitTimeout, ct);
        Assert.Equal(2, _nextCount);
    }

    [Fact]
    public async Task 待っている間に一時停止すると切り替えない()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _systemState.IsOnBatteryPower().Returns(true);
        _sut.Start();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.OnBattery);

        await _sut.PauseAsync(ct);
        _systemState.IsOnBatteryPower().Returns(false);
        await WaitForTimerAsync();
        _time.Advance(WallpaperScheduler.HoldCheckInterval);

        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.None);
        await Task.Delay(100, ct);
        Assert.Equal(0, _nextCount);
    }

    [Fact]
    public async Task 待っている間に即時変更すると待つのをやめる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _systemState.IsFullScreenAppRunning().Returns(true);
        _sut.Start();
        await WaitForTimerAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.FullScreen);

        await _sut.ChangeNowAsync(ct);

        await WaitUntilAsync(() => _sut.Hold == AutoChangeHold.None);
        Assert.Equal(1, _nextCount);
    }

    [Fact]
    public async Task StopAsyncで停止する()
    {
        _sut.Start();
        Assert.True(_sut.IsRunning);

        await _sut.StopAsync();

        Assert.False(_sut.IsRunning);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// ループが PeriodicTimer の待機に入るのを待つ。FakeTimeProvider は待機開始前に Advance すると時刻が進むだけになるため。
    /// </summary>
    private static async Task WaitForTimerAsync()
    {
        // 少なくとも 1 つのタイマーが登録されるまで待つ（FakeTimeProvider は内部に待機者を保持する）
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow + WaitTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException();
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
