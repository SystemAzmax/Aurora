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
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_filePath)!, "*.corrupt"));
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

    private JsonSettingsService CreateSut() => new(
        Options.Create(new SettingsStorageOptions { FilePath = _filePath }),
        NullLogger<JsonSettingsService>.Instance);
}
