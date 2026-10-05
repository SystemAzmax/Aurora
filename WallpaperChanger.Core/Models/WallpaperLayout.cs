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
}

public static class WallpaperLayoutExtensions
{
    /// <summary>レイアウトの列数・行数。</summary>
    public static (int Columns, int Rows) GetGridSize(this WallpaperLayout layout) => layout switch
    {
        WallpaperLayout.SingleImage => (1, 1),
        WallpaperLayout.Grid2x2 => (2, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "未対応のレイアウトです。"),
    };

    /// <summary>レイアウトに必要な画像の枚数。</summary>
    public static int GetTileCount(this WallpaperLayout layout)
    {
        (int columns, int rows) = layout.GetGridSize();
        return columns * rows;
    }
}
