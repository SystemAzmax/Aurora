using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Infrastructure.Imaging;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Infrastructure;

/// <summary>
/// 実際に画像ファイルを生成・合成し、出力画像のピクセルを検証する。
/// </summary>
public sealed class WicWallpaperComposerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly string _outputDirectory;

    public WicWallpaperComposerTests()
    {
        _outputDirectory = Path.Combine(_temp.Path, "out");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task 四枚をモニター解像度の2x2に合成する()
    {
        // 縦横比の異なる単色画像（png / jpg / bmp / png）
        string[] images =
        [
            CreateImage("red.png", 300, 300, Colors.Red, new PngBitmapEncoder()),
            CreateImage("green.jpg", 800, 200, Colors.Lime, new JpegBitmapEncoder { QualityLevel = 100 }),
            CreateImage("blue.bmp", 100, 400, Colors.Blue, new BmpBitmapEncoder()),
            CreateImage("white.png", 1920, 1080, Colors.White, new PngBitmapEncoder()),
        ];
        WicWallpaperComposer sut = CreateSut(gap: 10);

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", images, 2, 2, 800, 600), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        Assert.Equal(800, result.PixelWidth);
        Assert.Equal(600, result.PixelHeight);
        AssertColor(result, 200, 150, Colors.Red);
        AssertColor(result, 600, 150, Colors.Lime);
        AssertColor(result, 200, 450, Colors.Blue);
        AssertColor(result, 600, 450, Colors.White);
        AssertColor(result, 400, 300, Color.FromRgb(0x20, 0x20, 0x20)); // タイル間の間隔
        // タイルの端まで画像で埋まっている（切り抜きで隙間ができない）
        AssertColor(result, 1, 1, Colors.Red);
        AssertColor(result, 798, 598, Colors.White);
    }

    [Fact]
    public async Task 画像の無いマスは真っ黒に塗る()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(gap: 10);

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red, null, null, red], 2, 2, 400, 200), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 100, 50, Colors.Red);
        AssertColor(result, 300, 50, Colors.Black);
        AssertColor(result, 100, 150, Colors.Black);
        AssertColor(result, 300, 150, Colors.Red);
        AssertColor(result, 200, 50, Color.FromRgb(0x20, 0x20, 0x20)); // 間隔は黒いマスと区別できる色のまま
    }

    [Fact]
    public async Task 画像が4枚未満なら繰り返して配置する()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(gap: 0);

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 2, 2, 200, 100), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 150, 75, Colors.Red);
    }

    /// <summary>
    /// 保存されている画像（200×100）は 左上: 赤 / 右上: 青 / 左下: 緑 / 右下: 黄。
    /// 撮影時の向き（EXIF Orientation）に従って回転・反転した後の四隅の色を検証する。
    /// </summary>
    [Theory]
    [InlineData(1, "R", "B", "G", "Y")] // そのまま
    [InlineData(2, "B", "R", "Y", "G")] // 左右反転
    [InlineData(3, "Y", "G", "B", "R")] // 180° 回転
    [InlineData(4, "G", "Y", "R", "B")] // 上下反転
    [InlineData(5, "R", "G", "B", "Y")] // 転置
    [InlineData(6, "G", "R", "Y", "B")] // 時計回りに 90° 回転
    [InlineData(7, "Y", "B", "G", "R")] // 反転置
    [InlineData(8, "B", "Y", "R", "G")] // 時計回りに 270° 回転
    public async Task 撮影時の向きに従って回転反転してから配置する(
        int orientation, string topLeft, string topRight, string bottomLeft, string bottomRight)
    {
        string image = CreateQuadrantJpeg($"oriented{orientation}.jpg", orientation);
        WicWallpaperComposer sut = CreateSut(gap: 0);

        // 縦横が入れ替わる向きは縦長の 1 マスに、それ以外は横長の 1 マスに、拡大縮小せずに収まる大きさで合成する
        (int width, int height) = orientation >= 5 ? (100, 200) : (200, 100);
        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [image], 1, 1, width, height), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, width / 4, height / 4, QuadrantColor(topLeft));
        AssertColor(result, width * 3 / 4, height / 4, QuadrantColor(topRight));
        AssertColor(result, width / 4, height * 3 / 4, QuadrantColor(bottomLeft));
        AssertColor(result, width * 3 / 4, height * 3 / 4, QuadrantColor(bottomRight));
    }

    [Fact]
    public async Task 読み込めない画像はパス付きの例外になる()
    {
        string broken = _temp.CreateFile("broken.jpg", "not an image");
        WicWallpaperComposer sut = CreateSut(gap: 0);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [broken], 2, 2, 200, 100), TestContext.Current.CancellationToken));

        Assert.Contains(broken, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ピクセル数が上限を超える画像はデコードせず理由付きの例外になる()
    {
        string image = CreateImage("big.png", 100, 100, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(gap: 0, new ImageSourceLimits { MaxPixelCount = (100 * 100) - 1 });

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [image], 2, 2, 200, 100), TestContext.Current.CancellationToken));

        Assert.Contains(image, ex.Message, StringComparison.Ordinal);
        Assert.Contains("100×100", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_outputDirectory) && Directory.GetFiles(_outputDirectory).Length > 0);
    }

    [Fact]
    public async Task ファイルサイズが上限を超える画像は理由付きの例外になる()
    {
        string image = CreateImage("big.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(gap: 0, new ImageSourceLimits { MaxFileBytes = new FileInfo(image).Length - 1 });

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [image], 2, 2, 200, 100), TestContext.Current.CancellationToken));

        Assert.Contains(image, ex.Message, StringComparison.Ordinal);
        Assert.Contains("ファイルサイズ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 上限ちょうどの画像は合成できる()
    {
        string image = CreateImage("exact.png", 100, 100, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(
            gap: 0, new ImageSourceLimits { MaxPixelCount = 100 * 100, MaxFileBytes = new FileInfo(image).Length });

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [image], 2, 2, 200, 100), TestContext.Current.CancellationToken);

        AssertColor(Load(output), 50, 25, Colors.Red);
    }

    [Fact]
    public async Task 毎回別名で保存し古い合成画像は指定数だけ残す()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(gap: 0);
        var request = new WallpaperCompositionRequest("MON1", [red], 2, 2, 100, 100);
        var outputs = new List<string>();

        for (int i = 0; i < 5; i++)
        {
            outputs.Add(await sut.ComposeAsync(request, TestContext.Current.CancellationToken));
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(5, outputs.Distinct().Count());
        string[] remaining = Directory.GetFiles(_outputDirectory, "*.jpg");
        Assert.Equal(3, remaining.Length);
        Assert.Contains(outputs[^1], remaining);
        Assert.DoesNotContain(outputs[0], remaining);
    }

    [Fact]
    public async Task 別のモニターの合成画像は削除しない()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(gap: 0);

        string other = await sut.ComposeAsync(new("MON2", [red], 2, 2, 100, 100), TestContext.Current.CancellationToken);
        for (int i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await sut.ComposeAsync(new("MON1", [red], 2, 2, 100, 100), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(other));
    }

    private WicWallpaperComposer CreateSut(int gap, ImageSourceLimits? limits = null) => new(
        Options.Create(new WallpaperCompositionOptions { OutputDirectory = _outputDirectory, TileGap = gap }),
        Options.Create(limits ?? new ImageSourceLimits()),
        _time,
        NullLogger<WicWallpaperComposer>.Instance);

    private string CreateImage(string name, int width, int height, Color color, BitmapEncoder encoder)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 0xFF;
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        string path = Path.Combine(_temp.Path, name);
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    /// <summary>200×100 の 4 色の画像を、EXIF の Orientation を付けて JPEG で保存する。</summary>
    internal static string CreateQuadrantJpeg(string directory, string name, int orientation)
    {
        const int width = 200;
        const int height = 100;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = QuadrantColor((x < width / 2, y < height / 2) switch
                {
                    (true, true) => "R",
                    (false, true) => "B",
                    (true, false) => "G",
                    _ => "Y",
                });
                int i = ((y * width) + x) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 0xFF;
            }
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("System.Photo.Orientation", (ushort)orientation);

        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));

        string path = Path.Combine(directory, name);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private string CreateQuadrantJpeg(string name, int orientation) => CreateQuadrantJpeg(_temp.Path, name, orientation);

    private static Color QuadrantColor(string name) => name switch
    {
        "R" => Colors.Red,
        "B" => Colors.Blue,
        "G" => Colors.Lime,
        "Y" => Colors.Yellow,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static FormatConvertedBitmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr32, null, 0);
    }

    private static void AssertColor(BitmapSource bitmap, int x, int y, Color expected)
    {
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        var actual = Color.FromRgb(pixel[2], pixel[1], pixel[0]);

        // JPEG の圧縮誤差を許容する
        const int tolerance = 24;
        Assert.True(
            Math.Abs(actual.R - expected.R) <= tolerance
            && Math.Abs(actual.G - expected.G) <= tolerance
            && Math.Abs(actual.B - expected.B) <= tolerance,
            $"({x},{y}) の色が想定と異なります。expected={expected}, actual={actual}");
    }
}
