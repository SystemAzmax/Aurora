using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
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
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", images, 2, 2, 800, 600, Gap: 10, GapColor: 0x202020),
            TestContext.Current.CancellationToken);

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
    public async Task 一枚表示の画面を埋める表示では画面全体を覆い余白を残さない()
    {
        string red = CreateImage("red.png", 60, 40, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        // 60×40 を 200×100 に: 幅に合わせて 200×133 に拡大し、上下を切り抜く
        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 1, 1, 200, 100, ImageFit.Fill), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        Assert.Equal((200, 100), (result.PixelWidth, result.PixelHeight));
        AssertColor(result, 1, 1, Colors.Red);
        AssertColor(result, 198, 98, Colors.Red);
    }

    [Fact]
    public async Task 一枚表示の全体表示では画像全体を収めて余白を黒にする()
    {
        string red = CreateImage("red.png", 60, 40, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        // 60×40 を 200×100 に: 高さに合わせて 150×100 に拡大し、左右に 25 ずつ余白
        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 1, 1, 200, 100, ImageFit.Fit), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 100, 50, Colors.Red);
        AssertColor(result, 30, 2, Colors.Red);
        AssertColor(result, 170, 97, Colors.Red);
        AssertColor(result, 10, 50, Colors.Black);
        AssertColor(result, 190, 50, Colors.Black);
    }

    [Fact]
    public async Task 一枚表示の引き伸ばしでは縦横比を変えて画面いっぱいにする()
    {
        string red = CreateImage("red.png", 60, 40, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 1, 1, 200, 100, ImageFit.Stretch), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 1, 1, Colors.Red);
        AssertColor(result, 198, 98, Colors.Red);
    }

    [Fact]
    public async Task 一枚表示の中央表示では拡大せずに中央に置き余白を黒にする()
    {
        string red = CreateImage("red.png", 60, 40, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        // 60×40 のまま、200×100 の中央（左上が (70, 30)）に置く
        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 1, 1, 200, 100, ImageFit.Center), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 75, 35, Colors.Red);
        AssertColor(result, 124, 64, Colors.Red);
        AssertColor(result, 62, 50, Colors.Black);
        AssertColor(result, 137, 50, Colors.Black);
        AssertColor(result, 100, 22, Colors.Black);
        AssertColor(result, 100, 77, Colors.Black);
    }

    [Fact]
    public async Task 一枚表示の中央表示では画面より大きい画像の中央を切り抜く()
    {
        // 左半分が赤、右半分が青の 400×100 の画像を 200×100 の中央に置くと、中央の 200 ピクセル分だけが見える
        string image = CreateSplitImage("split.png", 400, 100, Colors.Red, Colors.Blue);
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [image], 1, 1, 200, 100, ImageFit.Center), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 1, 50, Colors.Red);
        AssertColor(result, 94, 50, Colors.Red);
        AssertColor(result, 106, 50, Colors.Blue);
        AssertColor(result, 198, 50, Colors.Blue);
    }

    [Fact]
    public async Task 画像の無いマスは真っ黒に塗る()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red, null, null, red], 2, 2, 400, 200, Gap: 10, GapColor: 0x202020),
            TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 100, 50, Colors.Red);
        AssertColor(result, 300, 50, Colors.Black);
        AssertColor(result, 100, 150, Colors.Black);
        AssertColor(result, 300, 150, Colors.Red);
        AssertColor(result, 200, 50, Color.FromRgb(0x20, 0x20, 0x20)); // 間隔は黒いマスと区別できる色のまま
    }

    [Fact]
    public async Task 間隔と空きマスの色を指定できる()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red, null, null, red], 2, 2, 400, 200, Gap: 20, GapColor: 0x0000FF, EmptyColor: 0xFFFFFF),
            TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 90, 45, Colors.Red);
        AssertColor(result, 310, 45, Colors.White);
        AssertColor(result, 200, 45, Colors.Blue);  // 縦の間隔
        AssertColor(result, 90, 100, Colors.Blue);  // 横の間隔
    }

    [Fact]
    public async Task 間隔を0にするとマスどうしが接する()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 2, 2, 400, 200, Gap: 0, GapColor: 0x0000FF),
            TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        AssertColor(result, 199, 100, Colors.Red);
        AssertColor(result, 200, 99, Colors.Red);
    }

    [Fact]
    public async Task 分割表示の全体表示ではマスごとに画像全体を収め余白を空きマスの色で塗る()
    {
        // 横長の画像（120×40）を、間隔なしの 2×2（各マス 200×200）に収める: 200×67 になり、上下に余白
        string red = CreateImage("wide.png", 120, 40, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [red], 2, 2, 400, 400, ImageFit.Fit, EmptyColor: 0xFFFFFF),
            TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        foreach ((int left, int top) in new[] { (0, 0), (200, 0), (0, 200), (200, 200) })
        {
            AssertColor(result, left + 100, top + 100, Colors.Red);
            AssertColor(result, left + 5, top + 100, Colors.Red);
            AssertColor(result, left + 100, top + 20, Colors.White);
            AssertColor(result, left + 100, top + 180, Colors.White);
        }
    }

    [Fact]
    public async Task 画像が4枚未満なら繰り返して配置する()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();

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
        WicWallpaperComposer sut = CreateSut();

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
    public async Task 十六分割では並行して読み込んだ画像をそれぞれのマスに配置する()
    {
        string[] images = [.. Enumerable.Range(0, 16).Select(i => CreateImage($"tile{i}.png", 64, 64, TileColor(i), new PngBitmapEncoder()))];
        WicWallpaperComposer sut = CreateSut();

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", images, 4, 4, 400, 400), TestContext.Current.CancellationToken);

        BitmapSource result = Load(output);
        for (int i = 0; i < 16; i++)
        {
            AssertColor(result, 50 + (i % 4 * 100), 50 + (i / 4 * 100), TileColor(i));
        }
    }

    [Fact]
    public async Task 並行して読み込んでも一枚ずつ読み込んだときと同じ画像になる()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string?[] images =
        [
            .. Enumerable.Range(0, 16).Select(i => i == 5 ? null : CreateImage($"tile{i}.jpg", 300 + (i * 37), 200 + (i * 23), TileColor(i), new JpegBitmapEncoder())),
        ];
        var request = new WallpaperCompositionRequest("MON1", images, 4, 4, 1280, 720, Gap: 4);

        string sequential = await CreateSut(maxParallelTileLoads: 1).ComposeAsync(request, ct);
        _time.Advance(TimeSpan.FromSeconds(1));
        string parallel = await CreateSut(maxParallelTileLoads: 4).ComposeAsync(request, ct);

        Assert.Equal(await File.ReadAllBytesAsync(sequential, ct), await File.ReadAllBytesAsync(parallel, ct));
    }

    [Fact]
    public async Task 並行して読み込む中に読めない画像があればパス付きの例外になる()
    {
        string broken = _temp.CreateFile("broken.jpg", "not an image");
        string[] images = [.. Enumerable.Range(0, 16).Select(i => i == 9 ? broken : CreateImage($"tile{i}.png", 64, 64, TileColor(i), new PngBitmapEncoder()))];
        WicWallpaperComposer sut = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", images, 4, 4, 400, 400), TestContext.Current.CancellationToken));

        Assert.Contains(broken, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 読み込めない画像はパス付きの例外になる()
    {
        string broken = _temp.CreateFile("broken.jpg", "not an image");
        WicWallpaperComposer sut = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [broken], 2, 2, 200, 100), TestContext.Current.CancellationToken));

        Assert.Contains(broken, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ピクセル数が上限を超える画像はデコードせず理由付きの例外になる()
    {
        string image = CreateImage("big.png", 100, 100, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut(new ImageSourceLimits { MaxPixelCount = (100 * 100) - 1 });

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
        WicWallpaperComposer sut = CreateSut(new ImageSourceLimits { MaxFileBytes = new FileInfo(image).Length - 1 });

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
            new ImageSourceLimits { MaxPixelCount = 100 * 100, MaxFileBytes = new FileInfo(image).Length });

        string output = await sut.ComposeAsync(
            new WallpaperCompositionRequest("MON1", [image], 2, 2, 200, 100), TestContext.Current.CancellationToken);

        AssertColor(Load(output), 50, 25, Colors.Red);
    }

    [Fact]
    public async Task 毎回別名で保存し古い合成画像は指定数だけ残す()
    {
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();
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
        WicWallpaperComposer sut = CreateSut();

        string other = await sut.ComposeAsync(new("MON2", [red], 2, 2, 100, 100), TestContext.Current.CancellationToken);
        for (int i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await sut.ComposeAsync(new("MON1", [red], 2, 2, 100, 100), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task 接続されていないモニターの合成画像は保存期間を過ぎたら削除する()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string red = CreateImage("red.png", 64, 64, Colors.Red, new PngBitmapEncoder());
        WicWallpaperComposer sut = CreateSut();
        string connectedOld = await sut.ComposeAsync(new("MON1", [red], 2, 2, 100, 100), ct);
        string disconnectedOld = await sut.ComposeAsync(new("MON2", [red], 2, 2, 100, 100), ct);
        string disconnectedRecent = await sut.ComposeAsync(new("MON3", [red], 2, 2, 100, 100), ct);
        DateTime now = _time.GetUtcNow().UtcDateTime;
        File.SetLastWriteTimeUtc(connectedOld, now - TimeSpan.FromDays(31));
        File.SetLastWriteTimeUtc(disconnectedOld, now - TimeSpan.FromDays(31));
        File.SetLastWriteTimeUtc(disconnectedRecent, now - TimeSpan.FromDays(29));

        sut.DeleteDisconnectedMonitorFiles(["mon1"]); // モニター ID の大文字小文字は区別しない

        Assert.True(File.Exists(connectedOld));       // 接続中のモニターは古くても残す（モニターごとの枚数で管理する）
        Assert.False(File.Exists(disconnectedOld));
        Assert.True(File.Exists(disconnectedRecent)); // 再接続に備えて保存期間内は残す
    }

    [Fact]
    public void 合成画像の保存先が無くても後片付けは例外にならない()
    {
        WicWallpaperComposer sut = CreateSut();

        sut.DeleteDisconnectedMonitorFiles(["MON1"]);

        Assert.False(Directory.Exists(_outputDirectory));
    }

    private WicWallpaperComposer CreateSut(ImageSourceLimits? limits = null, int maxParallelTileLoads = 4) => new(
        Options.Create(new WallpaperCompositionOptions
        {
            OutputDirectory = _outputDirectory,
            MaxParallelTileLoads = maxParallelTileLoads,
        }),
        Options.Create(limits ?? new ImageSourceLimits()),
        _time,
        NullLogger<WicWallpaperComposer>.Instance);

    /// <summary>16 マスを見分けられる色（隣どうしで、JPEG の誤差の許容範囲より大きく異なる）。</summary>
    private static Color TileColor(int index) => Color.FromRgb((byte)(index % 4 * 85), (byte)(index / 4 * 85), 128);

    /// <summary>左半分と右半分で色の異なる PNG 画像を作る。</summary>
    private string CreateSplitImage(string name, int width, int height, Color left, Color right)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Color color = i / 4 % width < width / 2 ? left : right;
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 0xFF;
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        string path = Path.Combine(_temp.Path, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

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
