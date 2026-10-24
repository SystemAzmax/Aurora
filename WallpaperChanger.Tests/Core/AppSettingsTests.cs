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

    [Fact]
    public void 十六分割では各マスが属する区画のフォルダを使う()
    {
        MonitorSettings monitor = MonitorSettings.CreateDefault("MON1").AddFolder(@"C:\common")
            with { Layout = WallpaperLayout.Grid4x4 };

        Assert.False(monitor.UsesTileFolders);
        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal([@"C:\common"], monitor.GetEffectiveTileFolders(i)));

        // 左下の区画 = 16 分割の 8, 9, 12, 13 番目のマス
        monitor = monitor.AddTileFolder(2, @"C:\lowerLeft");

        Assert.True(monitor.UsesTileFolders);
        int[] lowerLeft = [8, 9, 12, 13];
        Assert.All(lowerLeft, i => Assert.Equal([@"C:\lowerLeft"], monitor.GetEffectiveTileFolders(i)));
        Assert.All(Enumerable.Range(0, 16).Except(lowerLeft), i => Assert.Empty(monitor.GetEffectiveTileFolders(i)));
    }

    [Fact]
    public void 十六分割は区画のフォルダだけでも取得元がある()
    {
        MonitorSettings monitor = MonitorSettings.CreateDefault("MON1").AddTileFolder(3, @"C:\lowerRight")
            with { Layout = WallpaperLayout.Grid4x4 };

        Assert.True(monitor.HasAnyFolder());
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

    [Fact]
    public void 表示しない画像は重複せずに追加され大文字小文字を区別せずに判定し削除できる()
    {
        AppSettings settings = new AppSettings()
            .AddExcludedImage(@"C:\Pictures\a.jpg")
            .AddExcludedImage(@"c:\pictures\A.JPG");

        Assert.Equal([@"C:\Pictures\a.jpg"], settings.ExcludedImages);
        Assert.True(settings.IsExcluded(@"C:\PICTURES\A.jpg"));
        Assert.False(settings.IsExcluded(@"C:\Pictures\b.jpg"));

        settings = settings.RemoveExcludedImage(@"C:\PICTURES\A.JPG");

        Assert.Empty(settings.ExcludedImages);
    }

    [Fact]
    public void Normalizeは表示しない画像の空の値を除く()
    {
        var settings = new AppSettings { ExcludedImages = [@"C:\a.jpg", " ", null!] }.Normalize();

        Assert.Equal([@"C:\a.jpg"], settings.ExcludedImages);
    }

    [Fact]
    public void Normalizeは表示しない画像が無い設定を空にする()
    {
        var settings = new AppSettings { ExcludedImages = null! }.Normalize();

        Assert.Empty(settings.ExcludedImages);
    }
}
