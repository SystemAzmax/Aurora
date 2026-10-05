namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// モニター単位で壁紙を取得・設定する低レベルサービス（IDesktopWallpaper のラッパー）。
/// </summary>
public interface IWallpaperService
{
    /// <summary>指定モニターの壁紙を設定する。</summary>
    Task SetWallpaperAsync(string monitorId, string imagePath, CancellationToken cancellationToken = default);

    /// <summary>指定モニターに現在設定されている壁紙のパスを取得する。未設定の場合は null。</summary>
    Task<string?> GetWallpaperAsync(string monitorId, CancellationToken cancellationToken = default);
}
