using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 分割表示のタイル配置と、各画像の拡大縮小・切り抜きを計算する（描画処理から独立させてテスト可能にする）。
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
    /// 元画像をタイル全体を覆うように拡大縮小し、中央を切り抜くための値を返す。
    /// </summary>
    public static FillScaling CalculateFill(int sourceWidth, int sourceHeight, int tileWidth, int tileHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);

        double scale = Math.Max((double)tileWidth / sourceWidth, (double)tileHeight / sourceHeight);
        int scaledWidth = Math.Max(tileWidth, (int)Math.Ceiling(sourceWidth * scale));
        int scaledHeight = Math.Max(tileHeight, (int)Math.Ceiling(sourceHeight * scale));

        var crop = new PixelRect(
            (scaledWidth - tileWidth) / 2,
            (scaledHeight - tileHeight) / 2,
            tileWidth,
            tileHeight);

        return new FillScaling(scaledWidth, scaledHeight, crop);
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
