namespace WallpaperChanger.Core.Models;

/// <summary>
/// 1 モニター内の壁紙の配置。
/// </summary>
public enum WallpaperLayout
{
    /// <summary>1 枚表示。</summary>
    SingleImage = 0,

    /// <summary>画面を 4 分割（2 × 2）して 4 枚を表示。</summary>
    Grid2x2 = 1,

    /// <summary>画面を 16 分割（4 × 4）して 16 枚を表示。4 マスずつの区画（左上・右上・左下・右下）に分かれる。</summary>
    Grid4x4 = 2,
}

public static class WallpaperLayoutExtensions
{
    /// <summary>
    /// 分割表示のフォルダ設定の単位（左上・右上・左下・右下）の数。
    /// 4 分割では 1 マス、16 分割では 2 × 2 の 4 マスが 1 つの区画になる。
    /// </summary>
    public const int QuadrantCount = 4;

    /// <summary>レイアウトの列数・行数。</summary>
    public static (int Columns, int Rows) GetGridSize(this WallpaperLayout layout) => layout switch
    {
        WallpaperLayout.SingleImage => (1, 1),
        WallpaperLayout.Grid2x2 => (2, 2),
        WallpaperLayout.Grid4x4 => (4, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "未対応のレイアウトです。"),
    };

    /// <summary>レイアウトに必要な画像の枚数。</summary>
    public static int GetTileCount(this WallpaperLayout layout)
    {
        (int columns, int rows) = layout.GetGridSize();
        return columns * rows;
    }

    /// <summary>
    /// マスが属する区画（0: 左上、1: 右上、2: 左下、3: 右下）。
    /// </summary>
    /// <param name="tileIndex">左上から右方向、次の行へ、の順のマスの番号（0 始まり）。</param>
    public static int GetQuadrant(this WallpaperLayout layout, int tileIndex)
    {
        (int columns, int rows) = layout.GetGridSize();
        ArgumentOutOfRangeException.ThrowIfNegative(tileIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tileIndex, columns * rows);

        int column = tileIndex % columns;
        int row = tileIndex / columns;
        return (row * 2 / rows * 2) + (column * 2 / columns);
    }
}
