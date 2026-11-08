using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure.Settings;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _filePath;

    public JsonSettingsServiceTests()
    {
        _filePath = Path.Combine(_temp.Path, "sub", "settings.json");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ファイルが無い場合は既定値になる()
    {
        using JsonSettingsService sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.DefaultIntervalMinutes, sut.Current.IntervalMinutes);
        Assert.Empty(sut.Current.Monitors);
    }

    [Fact]
    public async Task 保存した設定を再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(
                s => s.WithInterval(90).WithMonitor(
                    MonitorSettings.CreateDefault(@"\\?\DISPLAY#ABC")
                        .AddFolder(@"C:\Pictures") with { IncludeSubfolders = false, SelectionMode = ImageSelectionMode.Sequential })
                    with { IsPaused = true },
                ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        Assert.Equal(90, reader.Current.IntervalMinutes);
        Assert.True(reader.Current.IsPaused);
        MonitorSettings monitor = Assert.Single(reader.Current.Monitors);
        Assert.Equal(@"\\?\DISPLAY#ABC", monitor.MonitorId);
        Assert.Equal([@"C:\Pictures"], monitor.Folders);
        Assert.False(monitor.IncludeSubfolders);
        Assert.Equal(ImageSelectionMode.Sequential, monitor.SelectionMode);
    }

    [Fact]
    public async Task モニターの名前を保存して再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(s => s.WithMonitor(MonitorSettings.CreateDefault("M") with { DisplayName = "LG ULTRAGEAR" }), ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        Assert.Equal("LG ULTRAGEAR", reader.Current.GetMonitor("M").DisplayName);
    }

    [Fact]
    public async Task 表示しない画像を保存して再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(s => s.AddExcludedImage(@"C:\w\a.jpg").AddExcludedImage(@"C:\w\b.jpg"), ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        Assert.Equal([@"C:\w\a.jpg", @"C:\w\b.jpg"], reader.Current.ExcludedImages);
    }

    [Fact]
    public async Task マスごとのフォルダとレイアウトを保存して再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(s => s.WithMonitor(MonitorSettings.CreateDefault("M")
                .AddTileFolder(1, @"C:\B")
                .AddTileFolder(3, @"C:\D") with { Layout = WallpaperLayout.Grid2x2 }), ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        MonitorSettings monitor = reader.Current.GetMonitor("M");
        Assert.Equal(WallpaperLayout.Grid2x2, monitor.Layout);
        Assert.Empty(monitor.GetTile(0).Folders);
        Assert.Equal([@"C:\B"], monitor.GetTile(1).Folders);
        Assert.Equal([@"C:\D"], monitor.GetTile(3).Folders);
    }

    [Fact]
    public async Task 一枚表示の表示方法を保存して再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(
                s => s
                    .WithMonitor(MonitorSettings.CreateDefault("FIT") with { SingleImageFit = ImageFit.Fit })
                    .WithMonitor(MonitorSettings.CreateDefault("WINDOWS")),
                ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        Assert.Equal(ImageFit.Fit, reader.Current.GetMonitor("FIT").SingleImageFit);
        Assert.Null(reader.Current.GetMonitor("WINDOWS").SingleImageFit);
        Assert.Contains("\"singleImageFit\": \"Fit\"", await File.ReadAllTextAsync(_filePath, ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 分割表示の収め方と間隔と色を保存して再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(
                s => s.WithMonitor(MonitorSettings.CreateDefault("M") with
                {
                    TileStyle = new TileStyle { Fit = ImageFit.Fit, Gap = 0, GapColor = 0xFFFFFF, EmptyColor = 0x808080 },
                }),
                ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        MonitorSettings monitor = reader.Current.GetMonitor("M");
        Assert.Equal(new TileStyle { Fit = ImageFit.Fit, Gap = 0, GapColor = 0xFFFFFF, EmptyColor = 0x808080 }, monitor.TileStyle);
    }

    [Fact]
    public async Task 分割表示の見た目の設定が無いファイルは既定値で読み込む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, """{ "monitors": [ { "monitorId": "M", "layout": "Grid2x2" } ] }""", ct);

        using JsonSettingsService sut = CreateSut();
        await sut.LoadAsync(ct);

        // この設定を追加する前の設定ファイルでも、今までと同じ見た目（4 px・濃い灰色の間隔、黒の空きマス）になる
        TileStyle style = sut.Current.GetMonitor("M").TileStyle;
        Assert.Equal((ImageFit.Fill, 4, 0x202020, 0x000000), (style.Fit, style.Gap, style.GapColor, style.EmptyColor));
    }

    [Fact]
    public async Task スパンの設定とレイアウトを保存して再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var span = new SpanSettings
        {
            IncludeSubfolders = false,
            SelectionMode = ImageSelectionMode.Sequential,
            Fit = ImageFit.Fit,
        };
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(
                s => s.WithMonitor(MonitorSettings.CreateDefault("M") with { Layout = WallpaperLayout.Span }) with
                {
                    Span = span.AddFolder(@"C:\Panorama"),
                },
                ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        Assert.Equal(WallpaperLayout.Span, reader.Current.GetMonitor("M").Layout);
        SpanSettings loaded = reader.Current.Span;
        Assert.Equal([@"C:\Panorama"], loaded.Folders);
        Assert.Equal((false, ImageSelectionMode.Sequential, ImageFit.Fit), (loaded.IncludeSubfolders, loaded.SelectionMode, loaded.Fit));
    }

    [Fact]
    public async Task スパンの設定が無いファイルは既定値で読み込む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, """{ "intervalMinutes": 30 }""", ct);

        using JsonSettingsService sut = CreateSut();
        await sut.LoadAsync(ct);

        Assert.Equal(new SpanSettings(), sut.Current.Span);
        Assert.True(sut.Current.Span.IncludeSubfolders);
    }

    [Fact]
    public async Task 自動切り替えを見合わせる条件を保存して読み込む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(
                s => s with { SkipConditions = new SkipConditions { WhenFullScreen = false, OnBattery = true } }, ct);
        }

        using JsonSettingsService reader = CreateSut();
        await reader.LoadAsync(ct);

        Assert.Equal(new SkipConditions { WhenFullScreen = false, OnBattery = true }, reader.Current.SkipConditions);
    }

    [Fact]
    public async Task 自動切り替えを見合わせる条件が無いファイルは既定値で読み込む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, """{ "intervalMinutes": 30 }""", ct);

        using JsonSettingsService sut = CreateSut();
        await sut.LoadAsync(ct);

        Assert.True(sut.Current.SkipConditions.WhenFullScreen);
        Assert.True(sut.Current.SkipConditions.OnBattery);
    }

    [Fact]
    public async Task 表示言語を保存して読み込み無いファイルは日本語で読み込む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using (JsonSettingsService writer = CreateSut())
        {
            await writer.UpdateAsync(s => s with { Language = AppLanguage.English }, ct);
        }

        string json = await File.ReadAllTextAsync(_filePath, ct);
        Assert.Contains("\"English\"", json, StringComparison.Ordinal);

        using (JsonSettingsService reader = CreateSut())
        {
            await reader.LoadAsync(ct);
            Assert.Equal(AppLanguage.English, reader.Current.Language);
        }

        await File.WriteAllTextAsync(_filePath, """{ "intervalMinutes": 30 }""", ct);
        using JsonSettingsService old = CreateSut();
        await old.LoadAsync(ct);
        Assert.Equal(AppLanguage.Japanese, old.Current.Language);
    }

    [Fact]
    public async Task 保存内容はキャメルケースで列挙型は文字列になる()
    {
        using JsonSettingsService sut = CreateSut();

        await sut.UpdateAsync(s => s.WithMonitor(MonitorSettings.CreateDefault("M")), TestContext.Current.CancellationToken);

        string json = await File.ReadAllTextAsync(_filePath, TestContext.Current.CancellationToken);
        Assert.Contains("\"intervalMinutes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Random\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"interval\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("usesTileFolders", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hasFolderSettings", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 更新時にSettingsChangedが発生する()
    {
        using JsonSettingsService sut = CreateSut();
        AppSettings? received = null;
        sut.SettingsChanged += (_, s) => received = s;

        await sut.UpdateAsync(s => s.WithInterval(5), TestContext.Current.CancellationToken);

        Assert.Equal(5, received?.IntervalMinutes);
    }

    [Fact]
    public async Task 破損ファイルは退避して既定値で読み込む()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, "{ not json", TestContext.Current.CancellationToken);
        using JsonSettingsService sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.DefaultIntervalMinutes, sut.Current.IntervalMinutes);
        Assert.False(File.Exists(_filePath));
        string backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_filePath)!, "*.corrupt"));
        Assert.Contains(backup, sut.LoadWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 正常に読み込めた場合は警告が無い()
    {
        await SaveIntervalAsync(90);
        using JsonSettingsService sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(90, sut.Current.IntervalMinutes);
        Assert.Null(sut.LoadWarning);
    }

    [Fact]
    public async Task 一時的にロックされていても再試行して読み込む()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SaveIntervalAsync(90);
        using JsonSettingsService sut = CreateSut(readAttempts: 50, readRetryDelay: TimeSpan.FromMilliseconds(20));
        FileStream lockStream = LockExclusively();

        Task load = sut.LoadAsync(ct);
        await Task.Delay(100, ct);
        await lockStream.DisposeAsync();
        await load;

        Assert.Equal(90, sut.Current.IntervalMinutes);
        Assert.Null(sut.LoadWarning);
    }

    [Fact]
    public async Task 読み込めない場合は既定値で起動し元のファイルを退避してから保存する()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SaveIntervalAsync(90);
        string original = await File.ReadAllTextAsync(_filePath, ct);
        using JsonSettingsService sut = CreateSut();

        using (LockExclusively())
        {
            await sut.LoadAsync(ct);
        }

        Assert.Equal(AppSettings.DefaultIntervalMinutes, sut.Current.IntervalMinutes);
        Assert.NotNull(sut.LoadWarning);

        await sut.UpdateAsync(s => s.WithInterval(5), ct);

        string backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_filePath)!, "*.unreadable"));
        Assert.Equal(original, await File.ReadAllTextAsync(backup, ct));
        Assert.Contains("\"intervalMinutes\": 5", await File.ReadAllTextAsync(_filePath, ct), StringComparison.Ordinal);

        // 退避は 1 回だけ（2 回目以降の保存では退避しない）
        await sut.UpdateAsync(s => s.WithInterval(6), ct);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_filePath)!, "*.unreadable"));
    }

    [Fact]
    public async Task 元のファイルを退避できない間は保存せず設定も変えない()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SaveIntervalAsync(90);
        string original = await File.ReadAllTextAsync(_filePath, ct);
        using JsonSettingsService sut = CreateSut();

        using (LockExclusively())
        {
            await sut.LoadAsync(ct);
            await Assert.ThrowsAnyAsync<IOException>(() => sut.UpdateAsync(s => s.WithInterval(5), ct));
        }

        Assert.Equal(AppSettings.DefaultIntervalMinutes, sut.Current.IntervalMinutes);
        Assert.Equal(original, await File.ReadAllTextAsync(_filePath, ct));
    }

    [Fact]
    public async Task 破損ファイルを退避できない場合も起動し保存時に退避する()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, "{ not json", ct);
        using JsonSettingsService sut = CreateSut();

        // 読み取りは許可するが、移動（削除）は許可しない
        using (new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await sut.LoadAsync(ct);
        }

        Assert.Equal(AppSettings.DefaultIntervalMinutes, sut.Current.IntervalMinutes);
        Assert.NotNull(sut.LoadWarning);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_filePath)!, "*.corrupt"));

        await sut.UpdateAsync(s => s.WithInterval(5), ct);

        string backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_filePath)!, "*.unreadable"));
        Assert.Equal("{ not json", await File.ReadAllTextAsync(backup, ct));
    }

    [Fact]
    public async Task 範囲外の間隔は読み込み時に補正される()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, """{ "intervalMinutes": 0 }""", TestContext.Current.CancellationToken);
        using JsonSettingsService sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.MinIntervalMinutes, sut.Current.IntervalMinutes);
    }

    private JsonSettingsService CreateSut(int readAttempts = 2, TimeSpan? readRetryDelay = null) => new(
        Options.Create(new SettingsStorageOptions
        {
            FilePath = _filePath,
            ReadAttempts = readAttempts,
            ReadRetryDelay = readRetryDelay ?? TimeSpan.Zero,
        }),
        NullLogger<JsonSettingsService>.Instance);

    private async Task SaveIntervalAsync(int minutes)
    {
        using JsonSettingsService writer = CreateSut();
        await writer.UpdateAsync(s => s.WithInterval(minutes), TestContext.Current.CancellationToken);
    }

    /// <summary>他のプロセスが排他的に開いている状態を再現する。</summary>
    private FileStream LockExclusively() => new(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
}
