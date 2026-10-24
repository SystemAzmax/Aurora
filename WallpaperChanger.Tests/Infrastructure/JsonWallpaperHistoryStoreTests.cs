using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure.History;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

public sealed class JsonWallpaperHistoryStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _filePath;

    public JsonWallpaperHistoryStoreTests()
    {
        _filePath = Path.Combine(_temp.Path, "sub", "history.json");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ファイルが無い場合は空になる()
    {
        IReadOnlyList<WallpaperHistorySnapshot> loaded = await CreateSut().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task 保存した履歴を再読み込みできる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WallpaperSelection single = WallpaperSelection.ForSingleImage(@"C:\w\1.jpg");
        var grid = new WallpaperSelection(WallpaperLayout.Grid2x2, [@"C:\a\1.jpg", null, null, @"C:\d\1.jpg"]);
        await CreateSut().SaveAsync(
            [
                new WallpaperHistorySnapshot(@"\\?\DISPLAY#ABC", [single, grid], Position: 0),
                new WallpaperHistorySnapshot("MON2", [single], Position: 0),
            ],
            ct);

        IReadOnlyList<WallpaperHistorySnapshot> loaded = await CreateSut().LoadAsync(ct);

        Assert.Equal([@"\\?\DISPLAY#ABC", "MON2"], loaded.Select(h => h.MonitorId));
        WallpaperHistorySnapshot first = loaded[0];
        Assert.Equal(0, first.Position);
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.Items[0].HasSameContent(single));
        Assert.True(first.Items[1].HasSameContent(grid));
    }

    [Fact]
    public async Task 保存し直すと前の内容を置き換える()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WallpaperSelection selection = WallpaperSelection.ForSingleImage(@"C:\w\1.jpg");
        await CreateSut().SaveAsync([new WallpaperHistorySnapshot("MON1", [selection], 0)], ct);

        await CreateSut().SaveAsync([new WallpaperHistorySnapshot("MON2", [selection], 0)], ct);

        IReadOnlyList<WallpaperHistorySnapshot> loaded = await CreateSut().LoadAsync(ct);
        Assert.Equal("MON2", Assert.Single(loaded).MonitorId);
    }

    [Fact]
    public async Task 保存内容はキャメルケースで列挙型は文字列になり計算用のプロパティを含まない()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var grid = new WallpaperSelection(WallpaperLayout.Grid2x2, [@"C:\a\1.jpg", null, null, null]);

        await CreateSut().SaveAsync([new WallpaperHistorySnapshot("M", [grid], 0)], ct);

        string json = await File.ReadAllTextAsync(_filePath, ct);
        Assert.Contains("\"monitorId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Grid2x2\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shownImages", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isSingleImage", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "monitors": [ { "monitorId": "M", "items": [ { "layout": "Grid9x9", "images": [ "a" ] } ], "position": 0 } ] }""")]
    [InlineData("""{ "monitors": [ { "monitorId": "M", "items": [ { "layout": "SingleImage", "images": [ null ] } ], "position": 0 } ] }""")]
    [InlineData("""{ "monitors": [ { "monitorId": "M", "items": [ { "layout": "SingleImage" } ], "position": 0 } ] }""")]
    public async Task 読み込めない内容なら空になる(string content)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, content, ct);

        IReadOnlyList<WallpaperHistorySnapshot> loaded = await CreateSut().LoadAsync(ct);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task 保存に失敗しても例外にしない()
    {
        // 保存先と同じ名前のフォルダがあり、ファイルを置き換えられない
        Directory.CreateDirectory(_filePath);

        await CreateSut().SaveAsync(
            [new WallpaperHistorySnapshot("M", [WallpaperSelection.ForSingleImage(@"C:\w\1.jpg")], 0)],
            TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(_filePath));
    }

    private JsonWallpaperHistoryStore CreateSut() => new(
        Options.Create(new HistoryStorageOptions { FilePath = _filePath }),
        NullLogger<JsonWallpaperHistoryStore>.Instance);
}
