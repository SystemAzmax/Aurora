using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;

namespace WallpaperChanger.Tests.Core;

public class SpanLayoutTests
{
    private static readonly MonitorBounds Left = new(0, 0, 1920, 1080);
    private static readonly MonitorBounds Right = new(1920, 0, 3840, 1080);

    [Fact]
    public void 横に並んだモニターに画像を左右に分ける()
    {
        SpanPlacement placement = SpanLayout.Calculate(3840, 1080, [Left, Right], ImageFit.Fill);

        Assert.Equal((3840, 1080), (placement.ScaledWidth, placement.ScaledHeight));
        Assert.Equal(new SpanPart(new PixelRect(0, 0, 1920, 1080), 0, 0), placement.Parts[0]);
        Assert.Equal(new SpanPart(new PixelRect(1920, 0, 1920, 1080), 0, 0), placement.Parts[1]);
    }

    [Fact]
    public void メインより左にあるモニター負の座標も外接矩形の左端からの位置で分ける()
    {
        SpanPlacement placement = SpanLayout.Calculate(
            3840, 1080, [new MonitorBounds(0, 0, 1920, 1080), new MonitorBounds(-1920, 0, 0, 1080)], ImageFit.Fill);

        // 指定した順（メイン、左側のモニター）に返す
        Assert.Equal(new PixelRect(1920, 0, 1920, 1080), placement.Parts[0].Source);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), placement.Parts[1].Source);
    }

    [Fact]
    public void 画面を埋める場合ははみ出した上下を切り抜いて分ける()
    {
        // 16:9 の画像を 32:9 の範囲に: 幅を合わせて 3840×2160 にし、上下 540 ずつ切り抜く
        SpanPlacement placement = SpanLayout.Calculate(1920, 1080, [Left, Right], ImageFit.Fill);

        Assert.Equal((3840, 2160), (placement.ScaledWidth, placement.ScaledHeight));
        Assert.Equal(new PixelRect(0, 540, 1920, 1080), placement.Parts[0].Source);
        Assert.Equal(new PixelRect(1920, 540, 1920, 1080), placement.Parts[1].Source);
    }

    [Fact]
    public void 全体を表示する場合は余白を残して中央に置いて分ける()
    {
        // 16:9 の画像を 32:9 の範囲に: 高さを合わせて 1920×1080 のまま、左右に 960 ずつ余白
        SpanPlacement placement = SpanLayout.Calculate(1920, 1080, [Left, Right], ImageFit.Fit);

        Assert.Equal(new SpanPart(new PixelRect(0, 0, 960, 1080), 960, 0), placement.Parts[0]);
        Assert.Equal(new SpanPart(new PixelRect(960, 0, 960, 1080), 0, 0), placement.Parts[1]);
    }

    [Fact]
    public void 高さの違うモニターでは外接矩形の中の位置で分ける()
    {
        // 右側に縦向きのモニター（上に 420 はみ出す）: 外接矩形は 3000×1920
        MonitorBounds portrait = new(1920, -420, 3000, 1500);
        SpanPlacement placement = SpanLayout.Calculate(3000, 1920, [Left, portrait], ImageFit.Fill);

        Assert.Equal(new SpanPart(new PixelRect(0, 420, 1920, 1080), 0, 0), placement.Parts[0]);
        Assert.Equal(new SpanPart(new PixelRect(1920, 0, 1080, 1920), 0, 0), placement.Parts[1]);
    }

    [Fact]
    public void 余白だけが映るモニターは空の範囲になる()
    {
        MonitorBounds third = new(3840, 0, 5760, 1080);

        // 縦長の画像を全体表示すると、真ん中のモニターだけに映る
        SpanPlacement placement = SpanLayout.Calculate(100, 1080, [Left, Right, third], ImageFit.Fit);

        Assert.True(placement.Parts[0].IsEmpty);
        Assert.Equal(new SpanPart(new PixelRect(0, 0, 100, 1080), 910, 0), placement.Parts[1]);
        Assert.True(placement.Parts[2].IsEmpty);
    }

    [Fact]
    public void モニターが無ければ計算できない()
    {
        Assert.Throws<ArgumentException>(() => SpanLayout.Calculate(100, 100, [], ImageFit.Fill));
    }
}
