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
            return Task.CompletedTask;
        });

        _sut = new WallpaperScheduler(_rotation, _settings, _time, NullLogger<WallpaperScheduler>.Instance);
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
    public async Task 変更が失敗しても定期実行は継続する()
    {
        int calls = 0;
        _rotation.NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return Task.FromException(new InvalidOperationException("boom"));
            }

            _changed.TrySetResult();
            return Task.CompletedTask;
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
