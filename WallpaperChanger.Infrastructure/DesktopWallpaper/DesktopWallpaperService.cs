using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// IDesktopWallpaper COM API でモニター単位の壁紙を取得・設定する。
/// </summary>
internal sealed class DesktopWallpaperService(DesktopWallpaperComHost comHost) : IWallpaperService
{
    private readonly DesktopWallpaperComHost _comHost = comHost ?? throw new ArgumentNullException(nameof(comHost));

    public Task SetWallpaperAsync(string monitorId, string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        string fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("壁紙に指定した画像ファイルが見つかりません。", fullPath);
        }

        return _comHost.InvokeAsync(wallpaper => wallpaper.SetWallpaper(monitorId, fullPath), cancellationToken);
    }

    public async Task<string?> GetWallpaperAsync(string monitorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

        string path = await _comHost
            .InvokeAsync(wallpaper => wallpaper.GetWallpaper(monitorId), cancellationToken)
            .ConfigureAwait(false);

        // 単色背景の場合は空文字列が返る
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
