using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WallpaperChanger.Infrastructure.FileSystem;
using WallpaperChanger.Infrastructure.Imaging;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

public sealed class FileSystemImageProviderTests : IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly TempDirectory _temp = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<FileSystemImageProvider> _created = [];
    private readonly FileSystemImageProvider _sut;

    public FileSystemImageProviderTests()
    {
        _sut = CreateSut();
    }

    public void Dispose()
    {
        // フォルダの監視を止めてから一時フォルダを削除する
        foreach (FileSystemImageProvider provider in _created)
        {
            provider.Dispose();
        }

        _temp.Dispose();
    }

    [Fact]
    public async Task 対応形式の画像のみを列挙する()
    {
        string[] expected =
        [
            _temp.CreateFile("a.jpg"),
            _temp.CreateFile("b.JPEG"),
            _temp.CreateFile("c.png"),
            _temp.CreateFile("d.bmp"),
            _temp.CreateFile("e.webp"),
        ];
        _temp.CreateFile("note.txt");
        _temp.CreateFile("anim.gif");

        IReadOnlyList<string> images = await _sut.GetImagesAsync([_temp.Path], false, TestContext.Current.CancellationToken);

        Assert.Equal(expected, images);
    }

    [Fact]
    public async Task サブフォルダ検索の有無を切り替えられる()
    {
        string top = _temp.CreateFile("top.jpg");
        string nested = _temp.CreateFile(Path.Combine("sub", "deep", "nested.png"));
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Equal([top], await _sut.GetImagesAsync([_temp.Path], false, ct));
        Assert.Equal([nested, top], await _sut.GetImagesAsync([_temp.Path], true, ct));
    }

    [Fact]
    public async Task 存在しないフォルダは無視し重複を除外する()
    {
        string image = _temp.CreateFile("a.jpg");

        IReadOnlyList<string> images = await _sut.GetImagesAsync(
            [_temp.Path, _temp.Path, Path.Combine(_temp.Path, "missing")], true, TestContext.Current.CancellationToken);

        Assert.Equal([image], images);
    }

    [Fact]
    public void Existsは対応形式かつ存在するファイルのみtrue()
    {
        string image = _temp.CreateFile("a.png");
        string text = _temp.CreateFile("a.txt");

        Assert.True(_sut.Exists(image));
        Assert.False(_sut.Exists(text));
        Assert.False(_sut.Exists(Path.Combine(_temp.Path, "missing.png")));
    }

    [Fact]
    public async Task ファイルサイズが上限を超える画像は候補に含めない()
    {
        string small = _temp.CreateFile("small.jpg", "12345");
        _temp.CreateFile("large.jpg", "123456");
        FileSystemImageProvider sut = CreateSut(new ImageSourceLimits { MaxFileBytes = 5 });

        IReadOnlyList<string> images = await sut.GetImagesAsync([_temp.Path], false, TestContext.Current.CancellationToken);

        Assert.Equal([small], images);
    }

    // ---- キャッシュ ----

    [Fact]
    public async Task キャッシュの期間内は列挙結果を使い回し期間が過ぎたら列挙し直す()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FileSystemImageProvider sut = CreateSut(watchForChanges: false);
        string a = _temp.CreateFile("a.jpg");
        Assert.Equal([a], await sut.GetImagesAsync([_temp.Path], false, ct));

        string b = _temp.CreateFile("b.jpg");
        _time.Advance(CacheDuration - TimeSpan.FromSeconds(1));
        Assert.Equal([a], await sut.GetImagesAsync([_temp.Path], false, ct));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([a, b], await sut.GetImagesAsync([_temp.Path], false, ct));
    }

    [Fact]
    public async Task Invalidateすると次回は列挙し直す()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FileSystemImageProvider sut = CreateSut(watchForChanges: false);
        string a = _temp.CreateFile("a.jpg");
        await sut.GetImagesAsync([_temp.Path], false, ct);
        File.Delete(a);

        sut.Invalidate();

        Assert.Empty(await sut.GetImagesAsync([_temp.Path], false, ct));
    }

    [Fact]
    public async Task フォルダへの追加を検知したら期間内でも列挙し直す()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string a = _temp.CreateFile("a.jpg");
        Assert.Equal([a], await _sut.GetImagesAsync([_temp.Path], false, ct));

        string b = _temp.CreateFile("b.jpg");

        Assert.Equal([a, b], await WaitForImagesAsync(_sut, false, 2, ct));
    }

    [Fact]
    public async Task サブフォルダでの削除も検知して列挙し直す()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string a = _temp.CreateFile("a.jpg");
        string nested = _temp.CreateFile(Path.Combine("sub", "nested.png"));
        Assert.Equal([a, nested], await _sut.GetImagesAsync([_temp.Path], true, ct));

        File.Delete(nested);

        Assert.Equal([a], await WaitForImagesAsync(_sut, true, 1, ct));
    }

    /// <summary>フォルダの変更通知は非同期に届くため、期待する件数になるまで待つ。</summary>
    private async Task<IReadOnlyList<string>> WaitForImagesAsync(
        FileSystemImageProvider sut, bool includeSubfolders, int expectedCount, CancellationToken ct)
    {
        IReadOnlyList<string> images = [];
        for (int i = 0; i < 100; i++)
        {
            images = await sut.GetImagesAsync([_temp.Path], includeSubfolders, ct);
            if (images.Count == expectedCount)
            {
                break;
            }

            await Task.Delay(50, ct);
        }

        return images;
    }

    private FileSystemImageProvider CreateSut(ImageSourceLimits? limits = null, bool watchForChanges = true)
    {
        var provider = new FileSystemImageProvider(
            Options.Create(limits ?? new ImageSourceLimits()),
            Options.Create(new ImageProviderOptions { CacheDuration = CacheDuration, WatchForChanges = watchForChanges }),
            _time,
            NullLogger<FileSystemImageProvider>.Instance);
        _created.Add(provider);
        return provider;
    }
}
