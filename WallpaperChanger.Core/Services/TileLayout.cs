using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 分割表示のタイル配置と、各画像を枠に収める拡大縮小・切り抜きを計算する（描画処理から独立させてテスト可能にする）。
/// </summary>
public static class TileLayout
{
    /// <summary>
    /// 画面を columns × rows に分割したタイルの位置を返す（左上から右方向、次の行へ、の順）。
    /// 割り切れない端数は右端・下端のタイルに含める。
    /// </summary>
    public static IReadOnlyList<PixelRect> CalculateTiles(int width, int height, int columns, int rows, int gap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        int[] xs = Split(width, columns, gap, nameof(width));
        int[] ws = Sizes(width, columns, gap);
        int[] ys = Split(height, rows, gap, nameof(height));
        int[] hs = Sizes(height, rows, gap);

        var tiles = new List<PixelRect>(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                tiles.Add(new PixelRect(xs[column], ys[row], ws[column], hs[row]));
            }
        }

        return tiles;
    }

    /// <summary>
    /// 元画像を枠（画面またはマス）に指定の方法で収めるための、拡大縮小・切り抜き・配置位置を返す。
    /// 切り抜いた画像は枠の中央に置く。
    /// </summary>
    public static ImagePlacement CalculatePlacement(int sourceWidth, int sourceHeight, int tileWidth, int tileHeight, ImageFit fit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);

        (int scaledWidth, int scaledHeight) = fit switch
        {
            ImageFit.Fill => ScaleToFill(sourceWidth, sourceHeight, tileWidth, tileHeight),
            ImageFit.Fit => ScaleToFit(sourceWidth, sourceHeight, tileWidth, tileHeight),
            ImageFit.Stretch => (tileWidth, tileHeight),
            ImageFit.Center => (sourceWidth, sourceHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(fit), fit, "未対応の表示方法です。"),
        };

        // 枠からはみ出す分は中央で切り抜き、足りない分は中央に置いて余白を残す
        int cropWidth = Math.Min(scaledWidth, tileWidth);
        int cropHeight = Math.Min(scaledHeight, tileHeight);
        var crop = new PixelRect((scaledWidth - cropWidth) / 2, (scaledHeight - cropHeight) / 2, cropWidth, cropHeight);
        return new ImagePlacement(scaledWidth, scaledHeight, crop, (tileWidth - cropWidth) / 2, (tileHeight - cropHeight) / 2);
    }

    /// <summary>枠全体を覆う大きさ（縦横比を保ち、枠の幅・高さ以上にする）。</summary>
    private static (int Width, int Height) ScaleToFill(int sourceWidth, int sourceHeight, int tileWidth, int tileHeight)
    {
        double scale = Math.Max((double)tileWidth / sourceWidth, (double)tileHeight / sourceHeight);
        return (Math.Max(tileWidth, (int)Math.Ceiling(sourceWidth * scale)), Math.Max(tileHeight, (int)Math.Ceiling(sourceHeight * scale)));
    }

    /// <summary>枠に収まる大きさ（縦横比を保ち、枠の幅・高さ以下にする）。</summary>
    private static (int Width, int Height) ScaleToFit(int sourceWidth, int sourceHeight, int tileWidth, int tileHeight)
    {
        double scale = Math.Min((double)tileWidth / sourceWidth, (double)tileHeight / sourceHeight);
        return (Math.Clamp((int)Math.Round(sourceWidth * scale), 1, tileWidth), Math.Clamp((int)Math.Round(sourceHeight * scale), 1, tileHeight));
    }

    private static int[] Sizes(int length, int count, int gap)
    {
        int available = length - (gap * (count - 1));
        int baseSize = available / count;
        int[] sizes = Enumerable.Repeat(baseSize, count).ToArray();
        sizes[^1] += available - (baseSize * count);
        return sizes;
    }

    private static int[] Split(int length, int count, int gap, string paramName)
    {
        if (length - (gap * (count - 1)) < count)
        {
            throw new ArgumentOutOfRangeException(paramName, length, "分割数と間隔に対して画面サイズが小さすぎます。");
        }

        int[] sizes = Sizes(length, count, gap);
        int[] offsets = new int[count];
        for (int i = 1; i < count; i++)
        {
            offsets[i] = offsets[i - 1] + sizes[i - 1] + gap;
        }

        return offsets;
    }
}
