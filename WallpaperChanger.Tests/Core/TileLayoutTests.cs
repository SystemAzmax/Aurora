using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;

namespace WallpaperChanger.Tests.Core;

public class TileLayoutTests
{
    [Fact]
    public void 間隔なしの2x2は画面を均等に4分割する()
    {
        IReadOnlyList<PixelRect> tiles = TileLayout.CalculateTiles(1920, 1080, 2, 2, gap: 0);

        Assert.Equal(
            [new(0, 0, 960, 540), new(960, 0, 960, 540), new(0, 540, 960, 540), new(960, 540, 960, 540)],
            tiles);
    }

    [Fact]
    public void 間隔ありでは端数を右端と下端に含め画面全体を覆う()
    {
        IReadOnlyList<PixelRect> tiles = TileLayout.CalculateTiles(1921, 1081, 2, 2, gap: 4);

        Assert.Equal(new PixelRect(0, 0, 958, 538), tiles[0]);
        Assert.Equal(new PixelRect(962, 0, 959, 538), tiles[1]);
        Assert.Equal(new PixelRect(0, 542, 958, 539), tiles[2]);
        Assert.Equal(1921, tiles[3].X + tiles[3].Width);
        Assert.Equal(1081, tiles[3].Y + tiles[3].Height);
    }

    [Fact]
    public void 一行一列は画面全体の1タイルになる()
    {
        Assert.Equal([new PixelRect(0, 0, 2560, 1440)], TileLayout.CalculateTiles(2560, 1440, 1, 1, gap: 8));
    }

    [Theory]
    // 横長の画像を正方形タイルへ: 高さを合わせて左右を切る
    [InlineData(2000, 1000, 500, 500, 1000, 500, 250, 0)]
    // 縦長の画像を横長タイルへ: 幅を合わせて上下を切る
    [InlineData(1000, 2000, 960, 540, 960, 1920, 0, 690)]
    // 同じ比率: 切り抜きなし
    [InlineData(3840, 2160, 960, 540, 960, 540, 0, 0)]
    public void Fillはタイル全体を覆うように拡大縮小し中央を切り抜く(
        int sw, int sh, int tw, int th, int scaledW, int scaledH, int cropX, int cropY)
    {
        ImagePlacement fill = TileLayout.CalculatePlacement(sw, sh, tw, th, ImageFit.Fill);

        Assert.Equal(scaledW, fill.ScaledWidth);
        Assert.Equal(scaledH, fill.ScaledHeight);
        Assert.Equal(new PixelRect(cropX, cropY, tw, th), fill.Crop);
        Assert.Equal((0, 0), (fill.OffsetX, fill.OffsetY));
    }

    [Theory]
    // 横長の画像を正方形の枠へ: 幅を合わせて上下に余白
    [InlineData(2000, 1000, 500, 500, 500, 250, 0, 125)]
    // 縦長の画像を横長の枠へ: 高さを合わせて左右に余白
    [InlineData(1000, 2000, 960, 540, 270, 540, 345, 0)]
    // 小さい画像も枠に合わせて拡大する
    [InlineData(160, 90, 1920, 1080, 1920, 1080, 0, 0)]
    public void Fitは画像全体が収まるように拡大縮小し中央に置く(
        int sw, int sh, int tw, int th, int scaledW, int scaledH, int offsetX, int offsetY)
    {
        ImagePlacement fit = TileLayout.CalculatePlacement(sw, sh, tw, th, ImageFit.Fit);

        Assert.Equal((scaledW, scaledH), (fit.ScaledWidth, fit.ScaledHeight));
        Assert.Equal(new PixelRect(0, 0, scaledW, scaledH), fit.Crop);
        Assert.Equal((offsetX, offsetY), (fit.OffsetX, fit.OffsetY));
    }

    [Fact]
    public void Stretchは縦横比を変えて枠いっぱいに引き伸ばす()
    {
        ImagePlacement stretch = TileLayout.CalculatePlacement(1000, 2000, 960, 540, ImageFit.Stretch);

        Assert.Equal(new ImagePlacement(960, 540, new PixelRect(0, 0, 960, 540), 0, 0), stretch);
    }

    [Theory]
    // 枠より小さい画像: 拡大せずに中央に置く
    [InlineData(100, 50, 400, 300, 0, 0, 100, 50, 150, 125)]
    // 枠より大きい画像: 縮小せずに中央を切り抜く
    [InlineData(1000, 800, 400, 300, 300, 250, 400, 300, 0, 0)]
    // 幅だけはみ出す画像: 左右を切り抜き、上下に余白
    [InlineData(1000, 100, 400, 300, 300, 0, 400, 100, 0, 100)]
    public void Centerは拡大縮小せずに中央に置きはみ出しを切り抜く(
        int sw, int sh, int tw, int th, int cropX, int cropY, int cropW, int cropH, int offsetX, int offsetY)
    {
        ImagePlacement center = TileLayout.CalculatePlacement(sw, sh, tw, th, ImageFit.Center);

        Assert.Equal((sw, sh), (center.ScaledWidth, center.ScaledHeight));
        Assert.Equal(new PixelRect(cropX, cropY, cropW, cropH), center.Crop);
        Assert.Equal((offsetX, offsetY), (center.OffsetX, center.OffsetY));
    }

    [Theory]
    [InlineData(WallpaperLayout.SingleImage, 1)]
    [InlineData(WallpaperLayout.Grid2x2, 4)]
    [InlineData(WallpaperLayout.Grid4x4, 16)]
    public void レイアウトごとの必要枚数(WallpaperLayout layout, int expected)
    {
        Assert.Equal(expected, layout.GetTileCount());
    }

    [Theory]
    [InlineData(WallpaperLayout.Grid2x2, 0, 0)]
    [InlineData(WallpaperLayout.Grid2x2, 1, 1)]
    [InlineData(WallpaperLayout.Grid2x2, 2, 2)]
    [InlineData(WallpaperLayout.Grid2x2, 3, 3)]
    [InlineData(WallpaperLayout.Grid4x4, 0, 0)]
    [InlineData(WallpaperLayout.Grid4x4, 5, 0)]
    [InlineData(WallpaperLayout.Grid4x4, 2, 1)]
    [InlineData(WallpaperLayout.Grid4x4, 7, 1)]
    [InlineData(WallpaperLayout.Grid4x4, 8, 2)]
    [InlineData(WallpaperLayout.Grid4x4, 13, 2)]
    [InlineData(WallpaperLayout.Grid4x4, 10, 3)]
    [InlineData(WallpaperLayout.Grid4x4, 15, 3)]
    public void マスが属する区画(WallpaperLayout layout, int tileIndex, int expected)
    {
        Assert.Equal(expected, layout.GetQuadrant(tileIndex));
    }

    [Fact]
    public void レイアウトの範囲外のマスの区画は求められない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WallpaperLayout.Grid2x2.GetQuadrant(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => WallpaperLayout.Grid4x4.GetQuadrant(-1));
    }
}
