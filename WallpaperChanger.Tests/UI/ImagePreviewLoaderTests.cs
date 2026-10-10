using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WallpaperChanger.Infrastructure.Imaging;
using WallpaperChanger.Tests.Fakes;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.Tests.UI;

public sealed class ImagePreviewLoaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task 上限内の画像は縮小して読み込む()
    {
        string image = CreatePng("ok.png", 200, 100);
        ImagePreviewLoader sut = CreateSut(new ImageSourceLimits { MaxPixelCount = 200 * 100 });

        ImageSource? result = await sut.LoadAsync(image, 50, TestContext.Current.CancellationToken);

        BitmapSource bitmap = Assert.IsAssignableFrom<BitmapSource>(result);
        Assert.Equal(50, bitmap.PixelWidth);
    }

    [Fact]
    public async Task ピクセル数が上限を超える画像は読み込まない()
    {
        string image = CreatePng("big.png", 200, 100);
        ImagePreviewLoader sut = CreateSut(new ImageSourceLimits { MaxPixelCount = (200 * 100) - 1 });

        Assert.Null(await sut.LoadAsync(image, 50, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ファイルサイズが上限を超える画像は読み込まない()
    {
        string image = CreatePng("big.png", 200, 100);
        ImagePreviewLoader sut = CreateSut(new ImageSourceLimits { MaxFileBytes = new FileInfo(image).Length - 1 });

        Assert.Null(await sut.LoadAsync(image, 50, TestContext.Current.CancellationToken));
    }

    private static ImagePreviewLoader CreateSut(ImageSourceLimits limits) =>
        new(Options.Create(limits), NullLogger<ImagePreviewLoader>.Instance);

    private string CreatePng(string name, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        string path = Path.Combine(_temp.Path, name);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
