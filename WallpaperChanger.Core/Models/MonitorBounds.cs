namespace WallpaperChanger.Core.Models;

/// <summary>
/// 仮想デスクトップ座標系におけるモニター領域。
/// </summary>
public readonly record struct MonitorBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
