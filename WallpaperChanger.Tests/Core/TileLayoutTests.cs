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
        FillScaling fill = TileLayout.CalculateFill(sw, sh, tw, th);

        Assert.Equal(scaledW, fill.ScaledWidth);
        Assert.Equal(scaledH, fill.ScaledHeight);
        Assert.Equal(new PixelRect(cropX, cropY, tw, th), fill.Crop);
    }

    [Theory]
    [InlineData(WallpaperLayout.SingleImage, 1)]
    [InlineData(WallpaperLayout.Grid2x2, 4)]
    public void レイアウトごとの必要枚数(WallpaperLayout layout, int expected)
    {
        Assert.Equal(expected, layout.GetTileCount());
    }
}
