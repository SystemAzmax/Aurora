namespace WallpaperChanger.Core.Models;

/// <summary>
/// ピクセル単位の矩形。
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// 画像をタイルに「隙間なく埋める（はみ出しは中央で切り抜く）」ための拡大縮小と切り抜き位置。
/// </summary>
/// <param name="ScaledWidth">拡大縮小後の幅（タイル幅以上）。</param>
/// <param name="ScaledHeight">拡大縮小後の高さ（タイル高さ以上）。</param>
/// <param name="Crop">拡大縮小後の画像から切り抜く範囲（サイズはタイルと同じ）。</param>
public readonly record struct FillScaling(int ScaledWidth, int ScaledHeight, PixelRect Crop);
