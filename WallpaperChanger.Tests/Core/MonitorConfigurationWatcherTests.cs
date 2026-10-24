using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Core;

public sealed class MonitorConfigurationWatcherTests : IDisposable
{
    private static readonly MonitorInfo Laptop = new("LAPTOP", 0, "Laptop", new MonitorBounds(0, 0, 1920, 1200), 1920, 1200);
    private static readonly MonitorInfo External = new("EXT", 1, "External", new MonitorBounds(-1920, 0, 0, 1080), 1920, 1080);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeDisplayChangeNotifier _notifier = new();
    private readonly IMonitorService _monitors = Substitute.For<IMonitorService>();
    private readonly IWallpaperRotationService _rotation = Substitute.For<IWallpaperRotationService>();
    private readonly IMonitorSettingsInheritor _inheritor = Substitute.For<IMonitorSettingsInheritor>();
    private readonly MonitorConfigurationWatcher _sut;
    private readonly List<MonitorConfigurationChangedEventArgs> _changes = [];

    public MonitorConfigurationWatcherTests()
    {
        _inheritor.InheritAsync(Arg.Any<IReadOnlyList<MonitorInfo>>(), Arg.Any<CancellationToken>()).Returns([]);
        _sut = new MonitorConfigurationWatcher(
            _notifier, _monitors, _rotation, _inheritor, _time, NullLogger<MonitorConfigurationWatcher>.Instance);
        _sut.ConfigurationChanged += (_, e) => _changes.Add(e);
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task 通知が続いている間は待ち落ち着いてから1回だけ処理する()
    {
        await StartWithAsync(Laptop);
        UseMonitors(Laptop, External);

        _notifier.Raise();
        _time.Advance(TimeSpan.FromSeconds(1));
        _notifier.Raise();
        _time.Advance(TimeSpan.FromSeconds(1));
        _notifier.Raise();
        Assert.Empty(_changes);

        await SettleAsync();

        MonitorConfigurationChangedEventArgs change = Assert.Single(_changes);
        Assert.Equal([External], change.Added);
        await _monitors.Received(2).GetMonitorsAsync(Arg.Any<CancellationToken>()); // 開始時 + 落ち着いた後の 1 回
    }

    [Fact]
    public async Task 新しく接続されたモニターに壁紙を表示する()
    {
        await StartWithAsync(Laptop);
        UseMonitors(Laptop, External);

        _notifier.Raise();
        await SettleAsync();

        await _rotation.Received(1).NextAsync("EXT", Arg.Any<CancellationToken>());
        await _rotation.DidNotReceive().NextAsync("LAPTOP", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 解像度が変わったモニターは表示し直す()
    {
        await StartWithAsync(Laptop, External);
        MonitorInfo resized = External with { Width = 2560, Height = 1440, Bounds = new MonitorBounds(-2560, 0, 0, 1440) };
        UseMonitors(Laptop, resized);

        _notifier.Raise();
        await SettleAsync();

        MonitorConfigurationChangedEventArgs change = Assert.Single(_changes);
        Assert.Equal([resized], change.Resized);
        await _rotation.Received(1).ReapplyAsync("EXT", Arg.Any<CancellationToken>());
        await _rotation.DidNotReceive().NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 切断と配置の変更は通知するが壁紙は変えない()
    {
        await StartWithAsync(Laptop, External);
        UseMonitors(Laptop with { Bounds = new MonitorBounds(0, 0, 1920, 1200) });

        _notifier.Raise();
        await SettleAsync();

        MonitorConfigurationChangedEventArgs change = Assert.Single(_changes);
        Assert.Equal([External], change.Removed);
        await _rotation.DidNotReceive().NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _rotation.DidNotReceive().ReapplyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // 配置だけの変更（位置が変わった）も一覧の更新のため通知する
        UseMonitors(Laptop with { Bounds = new MonitorBounds(1920, 0, 3840, 1200) });
        _notifier.Raise();
        await SettleAsync();

        Assert.Equal(2, _changes.Count);
        Assert.Empty(_changes[1].Added);
        Assert.Empty(_changes[1].Resized);
    }

    [Fact]
    public async Task 構成が変わっていなければ通知しない()
    {
        await StartWithAsync(Laptop, External);

        _notifier.Raise();
        await SettleAsync();

        Assert.Empty(_changes);
    }

    [Fact]
    public async Task 一台の更新に失敗しても他のモニターの更新は続ける()
    {
        MonitorInfo second = new("EXT2", 2, "External 2", new MonitorBounds(1920, 0, 3840, 1080), 1920, 1080);
        await StartWithAsync(Laptop);
        UseMonitors(Laptop, External, second);
        _rotation.NextAsync("EXT", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        _notifier.Raise();
        await SettleAsync();

        await _rotation.Received(1).NextAsync("EXT2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 開始時に接続中のモニターへ前の設定の引き継ぎを試みる()
    {
        await StartWithAsync(Laptop, External);

        await _inheritor.Received(1).InheritAsync(
            Arg.Is<IReadOnlyList<MonitorInfo>>(m => m.SequenceEqual(new[] { Laptop, External })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 新しく接続されたモニターは前の設定を引き継いでから壁紙を表示する()
    {
        await StartWithAsync(Laptop);
        UseMonitors(Laptop, External);
        _inheritor.InheritAsync(Arg.Any<IReadOnlyList<MonitorInfo>>(), Arg.Any<CancellationToken>()).Returns([External]);

        _notifier.Raise();
        await SettleAsync();

        Received.InOrder(() =>
        {
            _inheritor.InheritAsync(
                Arg.Is<IReadOnlyList<MonitorInfo>>(m => m.SequenceEqual(new[] { Laptop, External })), Arg.Any<CancellationToken>());
            _rotation.NextAsync("EXT", Arg.Any<CancellationToken>());
        });
        Assert.Equal([External], Assert.Single(_changes).Inherited);
    }

    [Fact]
    public async Task 設定を引き継げなくても新しく接続されたモニターに壁紙を表示する()
    {
        await StartWithAsync(Laptop);
        UseMonitors(Laptop, External);
        _inheritor.InheritAsync(Arg.Any<IReadOnlyList<MonitorInfo>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("書き込めません"));

        _notifier.Raise();
        await SettleAsync();

        await _rotation.Received(1).NextAsync("EXT", Arg.Any<CancellationToken>());
        Assert.Empty(Assert.Single(_changes).Inherited);
    }

    [Fact]
    public async Task 新しく接続されたモニターが無ければ設定の引き継ぎを試みない()
    {
        await StartWithAsync(Laptop, External);
        _inheritor.ClearReceivedCalls();
        UseMonitors(Laptop);

        _notifier.Raise();
        await SettleAsync();

        await _inheritor.DidNotReceive().InheritAsync(Arg.Any<IReadOnlyList<MonitorInfo>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 停止後の通知は処理しない()
    {
        await StartWithAsync(Laptop);
        await _sut.StopAsync();
        UseMonitors(Laptop, External);

        _notifier.Raise();
        await SettleAsync();

        Assert.Empty(_changes);
        Assert.False(_notifier.HasSubscribers);
    }

    private async Task StartWithAsync(params MonitorInfo[] monitors)
    {
        UseMonitors(monitors);
        await _sut.StartAsync(TestContext.Current.CancellationToken);
    }

    private void UseMonitors(params MonitorInfo[] monitors) =>
        _monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns(monitors);

    private async Task SettleAsync()
    {
        _time.Advance(MonitorConfigurationWatcher.SettleDelay);
        await _sut.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
