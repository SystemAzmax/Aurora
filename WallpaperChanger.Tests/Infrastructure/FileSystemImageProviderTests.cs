using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WallpaperChanger.Infrastructure.FileSystem;
using WallpaperChanger.Infrastructure.Imaging;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

public sealed class FileSystemImageProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileSystemImageProvider _sut = CreateSut(new ImageSourceLimits());

    public void Dispose() => _temp.Dispose();

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

    private static FileSystemImageProvider CreateSut(ImageSourceLimits limits) =>
        new(Options.Create(limits), NullLogger<FileSystemImageProvider>.Instance);
}
