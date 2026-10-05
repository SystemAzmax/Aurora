namespace WallpaperChanger.Core.Models;

/// <summary>
/// 接続中モニターの情報。
/// </summary>
/// <param name="Id">IDesktopWallpaper が返すモニターのデバイスパス。モニターを一意に識別する。</param>
/// <param name="Index">IDesktopWallpaper 上のインデックス（0 始まり）。</param>
/// <param name="DisplayName">表示用のモニター名。</param>
/// <param name="Bounds">仮想デスクトップ上の領域。</param>
/// <param name="Width">解像度（横）。</param>
/// <param name="Height">解像度（縦）。</param>
public sealed record MonitorInfo(
    string Id,
    int Index,
    string DisplayName,
    MonitorBounds Bounds,
    int Width,
    int Height)
{
    /// <summary>プライマリモニターは仮想デスクトップ原点に配置される。</summary>
    public bool IsPrimary => Bounds.Left == 0 && Bounds.Top == 0;

    public string ResolutionText => $"{Width} × {Height}";
}
