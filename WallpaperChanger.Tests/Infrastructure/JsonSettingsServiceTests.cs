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
    public async Task 保存内容はキャメルケースで列挙型は文字列になる()
    {
        using JsonSettingsService sut = CreateSut();

        await sut.UpdateAsync(s => s.WithMonitor(MonitorSettings.CreateDefault("M")), TestContext.Current.CancellationToken);

        string json = await File.ReadAllTextAsync(_filePath, TestContext.Current.CancellationToken);
        Assert.Contains("\"intervalMinutes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Random\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"interval\"", json, StringComparison.Ordinal);
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
