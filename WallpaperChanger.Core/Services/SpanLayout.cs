using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 複数モニターにまたがる 1 枚表示（スパン）で、各モニターに画像のどの範囲を表示するかを計算する（描画処理から独立させてテスト可能にする）。
/// </summary>
public static class SpanLayout
{
    /// <summary>
    /// 画像を、モニター全体を囲む範囲（仮想デスクトップ上の外接矩形）に指定の方法で収め、各モニターの位置で切り分ける。
    /// モニターの並びが長方形でない場合（高さの違うモニターなど）、外接矩形のうちモニターの無い部分の画像は表示されない。
    /// </summary>
    /// <param name="sourceWidth">画像の幅（撮影時の向きを反映した後）。</param>
    /// <param name="sourceHeight">画像の高さ（撮影時の向きを反映した後）。</param>
    /// <param name="monitors">各モニターの仮想デスクトップ上の領域。</param>
    /// <param name="fit">外接矩形への収め方。</param>
    public static SpanPlacement Calculate(int sourceWidth, int sourceHeight, IReadOnlyList<MonitorBounds> monitors, ImageFit fit)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("モニターを 1 台以上指定してください。", nameof(monitors));
        }

        if (monitors.Any(m => m.Width <= 0 || m.Height <= 0))
        {
            throw new ArgumentException("大きさの無いモニターは指定できません。", nameof(monitors));
        }

        int left = monitors.Min(m => m.Left);
        int top = monitors.Min(m => m.Top);
        int boxWidth = monitors.Max(m => m.Right) - left;
        int boxHeight = monitors.Max(m => m.Bottom) - top;
        ImagePlacement placement = TileLayout.CalculatePlacement(sourceWidth, sourceHeight, boxWidth, boxHeight, fit);

        // 外接矩形の中で画像が見えている範囲
        int imageLeft = placement.OffsetX;
        int imageTop = placement.OffsetY;
        int imageRight = imageLeft + placement.Crop.Width;
        int imageBottom = imageTop + placement.Crop.Height;

        var parts = new List<SpanPart>(monitors.Count);
        foreach (MonitorBounds monitor in monitors)
        {
            int monitorLeft = monitor.Left - left;
            int monitorTop = monitor.Top - top;
            int visibleLeft = Math.Max(monitorLeft, imageLeft);
            int visibleTop = Math.Max(monitorTop, imageTop);
            int visibleRight = Math.Min(monitorLeft + monitor.Width, imageRight);
            int visibleBottom = Math.Min(monitorTop + monitor.Height, imageBottom);

            if (visibleRight <= visibleLeft || visibleBottom <= visibleTop)
            {
                // 全体表示の余白だけが映るモニター
                parts.Add(new SpanPart(new PixelRect(0, 0, 0, 0), 0, 0));
                continue;
            }

            var source = new PixelRect(
                visibleLeft - imageLeft + placement.Crop.X,
                visibleTop - imageTop + placement.Crop.Y,
                visibleRight - visibleLeft,
                visibleBottom - visibleTop);
            parts.Add(new SpanPart(source, visibleLeft - monitorLeft, visibleTop - monitorTop));
        }

        return new SpanPlacement(placement.ScaledWidth, placement.ScaledHeight, parts);
    }
}

/// <summary>
/// スパンの切り分け結果。
/// </summary>
/// <param name="ScaledWidth">外接矩形に収めるために拡大縮小した後の画像の幅。</param>
/// <param name="ScaledHeight">外接矩形に収めるために拡大縮小した後の画像の高さ。</param>
/// <param name="Parts">モニターごとの切り抜き範囲（指定したモニターと同じ順）。</param>
public sealed record SpanPlacement(int ScaledWidth, int ScaledHeight, IReadOnlyList<SpanPart> Parts);

/// <summary>
/// 1 台のモニターに表示する、画像の範囲。
/// </summary>
/// <param name="Source">拡大縮小後の画像から切り抜く範囲。幅が 0 なら、このモニターには画像が映らない（余白だけ）。</param>
/// <param name="OffsetX">切り抜いた画像を置く位置（モニターの左端から）。</param>
/// <param name="OffsetY">切り抜いた画像を置く位置（モニターの上端から）。</param>
public readonly record struct SpanPart(PixelRect Source, int OffsetX, int OffsetY)
{
    public bool IsEmpty => Source.Width <= 0 || Source.Height <= 0;
}
