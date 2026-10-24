using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Core;

public sealed class MonitorSettingsInheritorTests
{
    private static readonly string OldPort = DevicePath("GSM7666", "UID4355");
    private static readonly string NewPort = DevicePath("GSM7666", "UID4357");
    private static readonly string ThirdPort = DevicePath("GSM7666", "UID4359");
    private static readonly string OtherModel = DevicePath("DEL4321", "UID4356");

    private readonly IWallpaperRotationService _rotation = Substitute.For<IWallpaperRotationService>();

    [Fact]
    public async Task 接続先を変えたモニターに前の設定と履歴を移す()
    {
        var settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(Configured(OldPort) with { Layout = WallpaperLayout.Grid2x2, DisplayName = "LG" }));
        MonitorInfo moved = Monitor(NewPort, "LG ULTRAGEAR");

        IReadOnlyList<MonitorInfo> inherited = await CreateSut(settings).InheritAsync([moved], TestContext.Current.CancellationToken);

        Assert.Equal([moved], inherited);
        MonitorSettings monitor = Assert.Single(settings.Current.Monitors);
        Assert.Equal(NewPort, monitor.MonitorId);
        Assert.Equal([@"C:\w"], monitor.Folders);
        Assert.Equal(WallpaperLayout.Grid2x2, monitor.Layout);
        Assert.Equal("LG ULTRAGEAR", monitor.DisplayName);
        await _rotation.Received(1).InheritHistoryAsync(OldPort, NewPort, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 新しいモニターにフォルダの無い設定があれば置き換える()
    {
        var settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(Configured(OldPort))
            .WithMonitor(MonitorSettings.CreateDefault(NewPort) with { Layout = WallpaperLayout.Grid4x4 }));

        await CreateSut(settings).InheritAsync([Monitor(NewPort)], TestContext.Current.CancellationToken);

        MonitorSettings monitor = Assert.Single(settings.Current.Monitors);
        Assert.Equal(NewPort, monitor.MonitorId);
        Assert.Equal([@"C:\w"], monitor.Folders);
        Assert.Equal(WallpaperLayout.SingleImage, monitor.Layout);
    }

    [Fact]
    public async Task 同じ型番のモニターの片方だけ接続先を変えたら引き継ぐ()
    {
        var settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(Configured(OldPort, @"C:\a"))
            .WithMonitor(Configured(NewPort, @"C:\b")));

        // NewPort のモニターを ThirdPort につなぎ替えた（OldPort のモニターはそのまま）
        await CreateSut(settings).InheritAsync([Monitor(OldPort), Monitor(ThirdPort)], TestContext.Current.CancellationToken);

        Assert.Equal([@"C:\a"], settings.Current.GetMonitor(OldPort).Folders);
        Assert.Equal([@"C:\b"], settings.Current.GetMonitor(ThirdPort).Folders);
        Assert.DoesNotContain(settings.Current.Monitors, m => m.MonitorId == NewPort);
    }

    [Fact]
    public async Task 型番が違うモニターには引き継がない()
    {
        var settings = new InMemorySettingsService(new AppSettings().WithMonitor(Configured(OldPort)));

        IReadOnlyList<MonitorInfo> inherited = await CreateSut(settings).InheritAsync(
            [Monitor(OtherModel)], TestContext.Current.CancellationToken);

        Assert.Empty(inherited);
        Assert.Equal(OldPort, Assert.Single(settings.Current.Monitors).MonitorId);
        Assert.Equal(0, settings.UpdateCount);
    }

    [Fact]
    public async Task 同じ型番の新しいモニターが複数あれば引き継がない()
    {
        var settings = new InMemorySettingsService(new AppSettings().WithMonitor(Configured(OldPort)));

        IReadOnlyList<MonitorInfo> inherited = await CreateSut(settings).InheritAsync(
            [Monitor(NewPort), Monitor(ThirdPort)], TestContext.Current.CancellationToken);

        Assert.Empty(inherited);
        Assert.Equal(OldPort, Assert.Single(settings.Current.Monitors).MonitorId);
    }

    [Fact]
    public async Task 同じ型番の前の設定が複数あれば引き継がない()
    {
        var settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(Configured(OldPort))
            .WithMonitor(Configured(ThirdPort)));

        IReadOnlyList<MonitorInfo> inherited = await CreateSut(settings).InheritAsync(
            [Monitor(NewPort)], TestContext.Current.CancellationToken);

        Assert.Empty(inherited);
        Assert.Equal(2, settings.Current.Monitors.Count);
        await _rotation.DidNotReceive().InheritHistoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 壁紙フォルダを設定したモニターには引き継がない()
    {
        var settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(Configured(OldPort, @"C:\old"))
            .WithMonitor(Configured(NewPort, @"C:\new")));

        IReadOnlyList<MonitorInfo> inherited = await CreateSut(settings).InheritAsync(
            [Monitor(NewPort)], TestContext.Current.CancellationToken);

        Assert.Empty(inherited);
        Assert.Equal([@"C:\new"], settings.Current.GetMonitor(NewPort).Folders);
    }

    [Fact]
    public async Task フォルダの無い前の設定は引き継がない()
    {
        var settings = new InMemorySettingsService(new AppSettings().WithMonitor(MonitorSettings.CreateDefault(OldPort)));

        IReadOnlyList<MonitorInfo> inherited = await CreateSut(settings).InheritAsync(
            [Monitor(NewPort)], TestContext.Current.CancellationToken);

        Assert.Empty(inherited);
    }

    [Fact]
    public async Task 接続中のモニターの名前を記録し変わらなければ保存しない()
    {
        var settings = new InMemorySettingsService(new AppSettings().WithMonitor(Configured(OldPort)));
        MonitorSettingsInheritor sut = CreateSut(settings);

        await sut.InheritAsync([Monitor(OldPort, "LG ULTRAGEAR"), Monitor(OtherModel, "DELL")], TestContext.Current.CancellationToken);
        await sut.InheritAsync([Monitor(OldPort, "LG ULTRAGEAR"), Monitor(OtherModel, "DELL")], TestContext.Current.CancellationToken);

        // 設定の無いモニター（DELL）は、名前を記録するためだけに追加しない
        MonitorSettings monitor = Assert.Single(settings.Current.Monitors);
        Assert.Equal("LG ULTRAGEAR", monitor.DisplayName);
        Assert.Equal(1, settings.UpdateCount);
    }

    private MonitorSettingsInheritor CreateSut(ISettingsService settings) =>
        new(settings, _rotation, NullLogger<MonitorSettingsInheritor>.Instance);

    private static MonitorSettings Configured(string monitorId, string folder = @"C:\w") =>
        MonitorSettings.CreateDefault(monitorId).AddFolder(folder);

    private static MonitorInfo Monitor(string id, string name = "Monitor") =>
        new(id, 0, name, new MonitorBounds(0, 0, 1920, 1080), 1920, 1080);

    private static string DevicePath(string hardwareId, string uid) =>
        $@"\\?\DISPLAY#{hardwareId}#5&14b0cd61&0&{uid}#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}";
}
