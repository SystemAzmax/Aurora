using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Tests.Core;

public class AppSettingsTests
{
    [Fact]
    public void 未登録モニターは既定値を返す()
    {
        var settings = new AppSettings();

        MonitorSettings monitor = settings.GetMonitor("MON1");

        Assert.Equal("MON1", monitor.MonitorId);
        Assert.Empty(monitor.Folders);
        Assert.True(monitor.IncludeSubfolders);
        Assert.Equal(ImageSelectionMode.Random, monitor.SelectionMode);
    }

    [Fact]
    public void WithMonitorは同じIDの設定を置き換える()
    {
        var settings = new AppSettings()
            .WithMonitor(MonitorSettings.CreateDefault("MON1"))
            .WithMonitor(MonitorSettings.CreateDefault("mon1") with { SelectionMode = ImageSelectionMode.Sequential });

        MonitorSettings monitor = Assert.Single(settings.Monitors);
        Assert.Equal(ImageSelectionMode.Sequential, monitor.SelectionMode);
    }

    [Fact]
    public void フォルダは重複せずに追加され削除できる()
    {
        MonitorSettings monitor = MonitorSettings.CreateDefault("MON1")
            .AddFolder(@"C:\Pictures\")
            .AddFolder(@"c:\pictures");

        Assert.Equal([@"C:\Pictures"], monitor.Folders);

        monitor = monitor.RemoveFolder(@"C:\PICTURES");

        Assert.Empty(monitor.Folders);
    }

    [Fact]
    public void マスの設定は足りない分を既定値で補って保存する()
    {
        MonitorSettings monitor = MonitorSettings.CreateDefault("MON1")
            .AddFolder(@"C:\common")
            .AddTileFolder(2, @"C:\tile2\");

        Assert.Equal(3, monitor.Tiles.Count);
        Assert.Empty(monitor.GetTile(0).Folders);
        Assert.Equal([@"C:\tile2"], monitor.GetTile(2).Folders);
        Assert.Empty(monitor.GetTile(3).Folders);

        monitor = monitor.RemoveTileFolder(2, @"c:\TILE2");
        Assert.Empty(monitor.GetTile(2).Folders);
    }

    [Fact]
    public void 全マス未設定なら1枚表示のフォルダを使い1つでも設定があれば未設定のマスは空になる()
    {
        MonitorSettings monitor = MonitorSettings.CreateDefault("MON1").AddFolder(@"C:\common")
            with { Layout = WallpaperLayout.Grid2x2 };

        Assert.False(monitor.UsesTileFolders);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal([@"C:\common"], monitor.GetEffectiveTileFolders(i)));

        monitor = monitor.AddTileFolder(2, @"C:\tile2");

        Assert.True(monitor.UsesTileFolders);
        Assert.Empty(monitor.GetEffectiveTileFolders(0));
        Assert.Equal([@"C:\tile2"], monitor.GetEffectiveTileFolders(2));
        Assert.False((monitor with { Layout = WallpaperLayout.SingleImage }).UsesTileFolders);
    }

    [Fact]
    public void マス専用フォルダは分割表示のときだけ取得元として数える()
    {
        MonitorSettings monitor = MonitorSettings.CreateDefault("MON1").AddTileFolder(0, @"C:\tile0");

        Assert.False(monitor.HasAnyFolder());
        Assert.True((monitor with { Layout = WallpaperLayout.Grid2x2 }).HasAnyFolder());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1440, true)]
    [InlineData(1441, false)]
    public void 切り替え間隔は1分から24時間(int minutes, bool expected)
    {
        Assert.Equal(expected, AppSettings.IsValidInterval(minutes));
    }

    [Fact]
    public void 範囲外の間隔はWithIntervalで例外になる()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings().WithInterval(0));
    }

    [Fact]
    public void Normalizeは範囲外の間隔を補正する()
    {
        var settings = new AppSettings { IntervalMinutes = 99999 }.Normalize();

        Assert.Equal(AppSettings.MaxIntervalMinutes, settings.IntervalMinutes);
    }
}
