namespace WallpaperChanger.Core.Models;

/// <summary>
/// 壁紙が変更されたことを通知するイベント引数。
/// </summary>
/// <param name="monitorId">対象モニター。</param>
/// <param name="imagePath">壁紙として設定したファイル（分割表示の場合は合成画像）。</param>
/// <param name="selection">表示した元画像の組。</param>
public sealed class WallpaperChangedEventArgs(string monitorId, string imagePath, WallpaperSelection selection) : EventArgs
{
    public string MonitorId { get; } = monitorId;

    public string ImagePath { get; } = imagePath;

    public WallpaperSelection Selection { get; } = selection;
}
