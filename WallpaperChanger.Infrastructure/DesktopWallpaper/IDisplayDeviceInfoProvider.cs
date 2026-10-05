namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// 表示デバイスのフレンドリ名と解像度。
/// </summary>
/// <param name="DevicePath">モニターのデバイスパス（IDesktopWallpaper のモニター ID と一致する）。</param>
/// <param name="IsInternal">ノート PC の内蔵パネルなど、内部接続の表示デバイスか。</param>
internal sealed record DisplayDeviceInfo(string DevicePath, string FriendlyName, int Width, int Height, bool IsInternal);

/// <summary>
/// アクティブな表示デバイスの情報を提供する。
/// </summary>
internal interface IDisplayDeviceInfoProvider
{
    /// <summary>デバイスパス（大文字小文字無視）をキーとした表示デバイス情報を取得する。</summary>
    IReadOnlyDictionary<string, DisplayDeviceInfo> GetActiveDisplays();
}
