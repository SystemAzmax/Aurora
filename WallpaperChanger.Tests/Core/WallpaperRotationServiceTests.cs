using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Core;

public sealed class WallpaperRotationServiceTests : IDisposable
{
    private static readonly MonitorInfo Monitor1 = new("MON1", 0, "Monitor 1", new MonitorBounds(0, 0, 1920, 1080), 1920, 1080);
    private static readonly MonitorInfo Monitor2 = new("MON2", 1, "Monitor 2", new MonitorBounds(1920, 0, 3840, 1080), 1920, 1080);
    private static readonly string[] Images = [@"C:\w\1.jpg", @"C:\w\2.jpg", @"C:\w\3.jpg"];
    private static readonly string[] CommonFolders = [@"C:\w"];

    private readonly IMonitorService _monitors = Substitute.For<IMonitorService>();
    private readonly IWallpaperService _wallpaper = Substitute.For<IWallpaperService>();
    private readonly IImageProvider _images = Substitute.For<IImageProvider>();
    private readonly IWallpaperComposer _composer = Substitute.For<IWallpaperComposer>();
    private readonly InMemorySettingsService _settings;
    private readonly WallpaperRotationService _sut;

    public WallpaperRotationServiceTests()
    {
        _monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([Monitor1, Monitor2]);
        _images.GetImagesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Images);
        _images.Exists(Arg.Any<string>()).Returns(true);
        _composer.ComposeAsync(Arg.Any<WallpaperCompositionRequest>(), Arg.Any<CancellationToken>()).Returns(@"C:\composite\grid.jpg");

        _settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(MonitorSettings.CreateDefault("MON1").AddFolder(@"C:\w") with { SelectionMode = ImageSelectionMode.Sequential })
            .WithMonitor(MonitorSettings.CreateDefault("MON2").AddFolder(@"C:\w") with { SelectionMode = ImageSelectionMode.Sequential }));

        _sut = new WallpaperRotationService(
            _monitors,
            _wallpaper,
            _images,
            _composer,
            _settings,
            [new SequentialImageSelector(), new RandomImageSelector()],
            NullLogger<WallpaperRotationService>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    // ---- 1 枚表示 ----

    [Fact]
    public async Task 順送りでは現在の壁紙の次の画像を設定する()
    {
        _wallpaper.GetWallpaperAsync("MON1", Arg.Any<CancellationToken>()).Returns(Images[0]);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        await _wallpaper.Received(1).SetWallpaperAsync("MON1", Images[1], Arg.Any<CancellationToken>());
        await _wallpaper.DidNotReceive().SetWallpaperAsync("MON2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task モニター未指定なら全モニターを変更する()
    {
        await _sut.NextAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _wallpaper.Received(1).SetWallpaperAsync("MON1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _wallpaper.Received(1).SetWallpaperAsync("MON2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task フォルダ未登録のモニターは変更しない()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(MonitorSettings.CreateDefault("MON2")), TestContext.Current.CancellationToken);

        await _sut.NextAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _wallpaper.DidNotReceive().SetWallpaperAsync("MON2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 一枚表示ではマス専用フォルダを使わない()
    {
        await _settings.UpdateAsync(
            s => s.WithMonitor(MonitorSettings.CreateDefault("MON2").AddTileFolder(0, @"C:\a")), TestContext.Current.CancellationToken);

        await _sut.NextAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _wallpaper.DidNotReceive().SetWallpaperAsync("MON2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 前へで直前の壁紙に戻り次へで履歴を進める()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _wallpaper.GetWallpaperAsync("MON1", Arg.Any<CancellationToken>()).Returns(Images[0]);

        await _sut.NextAsync("MON1", ct); // 1 -> 2
        await _sut.NextAsync("MON1", ct); // 2 -> 3
        _wallpaper.ClearReceivedCalls();

        await _sut.PreviousAsync("MON1", ct);
        await _wallpaper.Received(1).SetWallpaperAsync("MON1", Images[1], Arg.Any<CancellationToken>());

        await _sut.PreviousAsync("MON1", ct);
        await _wallpaper.Received(1).SetWallpaperAsync("MON1", Images[0], Arg.Any<CancellationToken>());

        _wallpaper.ClearReceivedCalls();
        _images.ClearReceivedCalls();
        await _sut.NextAsync("MON1", ct);
        await _wallpaper.Received(1).SetWallpaperAsync("MON1", Images[1], Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetImagesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 履歴が無ければ前へは何もしない()
    {
        await _sut.PreviousAsync("MON1", TestContext.Current.CancellationToken);

        await _wallpaper.DidNotReceive().SetWallpaperAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 変更時にWallpaperChangedイベントが発生する()
    {
        var raised = new List<WallpaperChangedEventArgs>();
        _sut.WallpaperChanged += (_, e) => raised.Add(e);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        WallpaperChangedEventArgs args = Assert.Single(raised);
        Assert.Equal("MON1", args.MonitorId);
        Assert.Equal(WallpaperLayout.SingleImage, args.Selection.Layout);
    }

    [Fact]
    public async Task 一部のモニターで失敗しても他のモニターは変更され例外は送出される()
    {
        _wallpaper.SetWallpaperAsync("MON1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => _sut.NextAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));
        await _wallpaper.Received(1).SetWallpaperAsync("MON2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 一枚表示では合成しない()
    {
        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        await _composer.DidNotReceive().ComposeAsync(Arg.Any<WallpaperCompositionRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- 4 分割表示 ----

    [Fact]
    public async Task 四分割では4枚を合成した画像を壁紙に設定する()
    {
        await UseGridLayoutAsync("MON1");

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        await _composer.Received(1).ComposeAsync(
            Arg.Is<WallpaperCompositionRequest>(r =>
                r.MonitorId == "MON1" && r.Columns == 2 && r.Rows == 2 && r.Width == 1920 && r.Height == 1080
                && r.ImagePaths.SequenceEqual(new[] { Images[0], Images[1], Images[2], Images[0] })),
            Arg.Any<CancellationToken>());
        await _wallpaper.Received(1).SetWallpaperAsync("MON1", @"C:\composite\grid.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 四分割で前へを押すと前の4枚の組を再合成する()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await UseGridLayoutAsync("MON1");
        var composed = new List<IReadOnlyList<string?>>();
        _composer.ComposeAsync(Arg.Do<WallpaperCompositionRequest>(r => composed.Add(r.ImagePaths)), Arg.Any<CancellationToken>())
            .Returns(@"C:\composite\grid.jpg");

        await _sut.NextAsync("MON1", ct);
        await _sut.NextAsync("MON1", ct);
        await _sut.PreviousAsync("MON1", ct);

        Assert.Equal(3, composed.Count);
        Assert.Equal(composed[0], composed[2]);
        Assert.NotEqual(composed[0], composed[1]);
    }

    [Fact]
    public async Task レイアウト変更後の次へは進む履歴を使わず選び直す()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _sut.NextAsync("MON1", ct);
        await _sut.NextAsync("MON1", ct);
        await _sut.PreviousAsync("MON1", ct);
        await UseGridLayoutAsync("MON1");

        await _sut.NextAsync("MON1", ct);

        await _composer.Received(1).ComposeAsync(Arg.Any<WallpaperCompositionRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- マスごとのフォルダ ----

    [Fact]
    public async Task マスに1つでもフォルダがあれば未設定のマスは黒になる()
    {
        string[] tileA = [@"C:\a\1.jpg", @"C:\a\2.jpg"];
        string[] tileD = [@"C:\d\1.jpg"];
        SetupFolder(@"C:\a", tileA);
        SetupFolder(@"C:\d", tileD);
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON1")
            .AddTileFolder(0, @"C:\a")
            .AddTileFolder(3, @"C:\d") with { Layout = WallpaperLayout.Grid2x2 }), TestContext.Current.CancellationToken);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        // 1 枚表示のフォルダ（C:\w）があっても、未設定の右上・左下は使わずに黒（null）にする
        Assert.Equal([tileA[0], null, null, tileD[0]], ComposedImages());
        await _images.DidNotReceive().GetImagesAsync(
            Arg.Is<IEnumerable<string>>(f => f.SequenceEqual(CommonFolders)), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 全マスが未設定なら1枚表示のフォルダから4枚を選ぶ()
    {
        await UseGridLayoutAsync("MON1");

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(null, ComposedImages());
    }

    [Fact]
    public async Task マスのフォルダに画像が無ければ黒にせず今の表示を残す()
    {
        SetupFolder(@"C:\a", [@"C:\a\1.jpg"]);
        SetupFolder(@"C:\empty", []);
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON1")
            .AddTileFolder(0, @"C:\a")
            .AddTileFolder(1, @"C:\empty") with { Layout = WallpaperLayout.Grid2x2 }), TestContext.Current.CancellationToken);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        await _composer.DidNotReceive().ComposeAsync(Arg.Any<WallpaperCompositionRequest>(), Arg.Any<CancellationToken>());
        await _wallpaper.DidNotReceive().SetWallpaperAsync("MON1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 黒のマスを含む組も前へで戻せる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SetupFolder(@"C:\a", [@"C:\a\1.jpg", @"C:\a\2.jpg"]);
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON1").AddTileFolder(0, @"C:\a") with
        {
            Layout = WallpaperLayout.Grid2x2,
            SelectionMode = ImageSelectionMode.Sequential,
        }), ct);
        var composed = new List<IReadOnlyList<string?>>();
        _composer.ComposeAsync(Arg.Do<WallpaperCompositionRequest>(r => composed.Add(r.ImagePaths)), Arg.Any<CancellationToken>())
            .Returns(@"C:\composite\grid.jpg");

        await _sut.NextAsync("MON1", ct);
        await _sut.NextAsync("MON1", ct);
        await _sut.PreviousAsync("MON1", ct);

        Assert.Equal([@"C:\a\1.jpg", null, null, null], composed[2]);
    }

    [Fact]
    public async Task マス専用フォルダも順送りで進む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string[] tileA = [@"C:\a\1.jpg", @"C:\a\2.jpg", @"C:\a\3.jpg"];
        SetupFolder(@"C:\a", tileA);
        await _settings.UpdateAsync(
            s => s.WithMonitor(s.GetMonitor("MON1").AddTileFolder(0, @"C:\a") with { Layout = WallpaperLayout.Grid2x2 }), ct);

        await _sut.NextAsync("MON1", ct);
        Assert.Equal(tileA[0], ComposedImages()[0]);
        _composer.ClearReceivedCalls();

        await _sut.NextAsync("MON1", ct);
        Assert.Equal(tileA[1], ComposedImages()[0]);
    }

    [Fact]
    public async Task 四分割でマスだけにフォルダがあれば共通フォルダが空でも変更する()
    {
        SetupFolder(@"C:\a", [@"C:\a\1.jpg"]);
        await _settings.UpdateAsync(s => s.WithMonitor(MonitorSettings.CreateDefault("MON1").AddTileFolder(2, @"C:\a") with
        {
            Layout = WallpaperLayout.Grid2x2,
        }), TestContext.Current.CancellationToken);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        await _wallpaper.Received(1).SetWallpaperAsync("MON1", @"C:\composite\grid.jpg", Arg.Any<CancellationToken>());
    }

    // ---- 表示し直し（解像度変更時） ----

    [Fact]
    public async Task 表示し直しは一枚表示では何もしない()
    {
        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);
        _wallpaper.ClearReceivedCalls();

        await _sut.ReapplyAsync("MON1", TestContext.Current.CancellationToken);

        await _wallpaper.DidNotReceive().SetWallpaperAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 表示し直しは四分割の同じ画像を今の解像度で合成し直す()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await UseGridLayoutAsync("MON1");
        var requests = new List<WallpaperCompositionRequest>();
        _composer.ComposeAsync(Arg.Do<WallpaperCompositionRequest>(requests.Add), Arg.Any<CancellationToken>())
            .Returns(@"C:\composite\grid.jpg");
        await _sut.NextAsync("MON1", ct);

        MonitorInfo resized = Monitor1 with { Width = 3840, Height = 2160, Bounds = new MonitorBounds(0, 0, 3840, 2160) };
        _monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([resized, Monitor2]);
        await _sut.ReapplyAsync("MON1", ct);

        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].ImagePaths, requests[1].ImagePaths);
        Assert.Equal((3840, 2160), (requests[1].Width, requests[1].Height));
        await _wallpaper.Received(2).SetWallpaperAsync("MON1", @"C:\composite\grid.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 表示し直しで表示中の組が無ければ次の壁紙を表示する()
    {
        await UseGridLayoutAsync("MON1");

        await _sut.ReapplyAsync("MON1", TestContext.Current.CancellationToken);

        await _composer.Received(1).ComposeAsync(Arg.Any<WallpaperCompositionRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- 16 分割 ----

    [Fact]
    public async Task 十六分割で区画が未設定なら1枚表示のフォルダから16枚を重複なく選び4x4で合成する()
    {
        string[] many = [.. Enumerable.Range(1, 20).Select(i => $@"C:\w\{i}.jpg")];
        SetupFolder(@"C:\w", many);
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON1") with
        {
            Layout = WallpaperLayout.Grid4x4,
            SelectionMode = ImageSelectionMode.Random,
        }), TestContext.Current.CancellationToken);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        await _composer.Received(1).ComposeAsync(
            Arg.Is<WallpaperCompositionRequest>(r => r.Columns == 4 && r.Rows == 4), Arg.Any<CancellationToken>());
        IReadOnlyList<string?> composed = ComposedImages();
        Assert.Equal(16, composed.Count);
        Assert.Equal(16, composed.Distinct().Count());
        Assert.All(composed, path => Assert.Contains(path, many));
    }

    [Fact]
    public async Task 十六分割では区画のフォルダを区画内の4マスに重複なく使い未設定の区画は黒になる()
    {
        string[] upperLeft = [.. Enumerable.Range(1, 6).Select(i => $@"C:\a\{i}.jpg")];
        string[] lowerRight = [.. Enumerable.Range(1, 4).Select(i => $@"C:\d\{i}.jpg")];
        SetupFolder(@"C:\a", upperLeft);
        SetupFolder(@"C:\d", lowerRight);
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON1")
            .AddTileFolder(0, @"C:\a")
            .AddTileFolder(3, @"C:\d") with
        {
            Layout = WallpaperLayout.Grid4x4,
            SelectionMode = ImageSelectionMode.Random,
        }), TestContext.Current.CancellationToken);

        await _sut.NextAsync("MON1", TestContext.Current.CancellationToken);

        IReadOnlyList<string?> composed = ComposedImages();
        int[] upperLeftTiles = [0, 1, 4, 5];
        int[] lowerRightTiles = [10, 11, 14, 15];
        string?[] fromA = [.. upperLeftTiles.Select(i => composed[i])];
        string?[] fromD = [.. lowerRightTiles.Select(i => composed[i])];
        Assert.All(fromA, path => Assert.Contains(path, upperLeft));
        Assert.Equal(4, fromA.Distinct().Count());
        Assert.Equal(lowerRight.Order(), fromD.Order());
        // 右上・左下の区画は未設定なので、1 枚表示のフォルダ（C:\w）は使わずに黒（null）にする
        Assert.All(Enumerable.Range(0, 16).Except(upperLeftTiles).Except(lowerRightTiles), i => Assert.Null(composed[i]));
    }

    private void SetupFolder(string folder, string[] images) =>
        _images.GetImagesAsync(
                Arg.Is<IEnumerable<string>>(f => f.SequenceEqual(new[] { folder })), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(images);

    private IReadOnlyList<string?> ComposedImages()
    {
        var call = _composer.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IWallpaperComposer.ComposeAsync));
        return ((WallpaperCompositionRequest)call.GetArguments()[0]!).ImagePaths;
    }

    private Task UseGridLayoutAsync(string monitorId) =>
        _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor(monitorId) with { Layout = WallpaperLayout.Grid2x2 }));
}
