namespace WallpaperChanger.Core.Models;

/// <summary>
/// 接続中のモニターと、このアプリがそのモニターに表示している画像の組。
/// </summary>
/// <param name="Monitor">接続中のモニター。</param>
/// <param name="Selection">表示している組（履歴の現在位置）。このアプリがまだ表示していなければ null。</param>
public sealed record CurrentWallpaper(MonitorInfo Monitor, WallpaperSelection? Selection);
