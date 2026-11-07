namespace WallpaperChanger.Core.Models;

/// <summary>
/// ピクセル単位の矩形。
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// 画像を枠（画面またはマス）に収めるための拡大縮小・切り抜き・配置位置。
/// </summary>
/// <param name="ScaledWidth">拡大縮小後の幅。</param>
/// <param name="ScaledHeight">拡大縮小後の高さ。</param>
/// <param name="Crop">拡大縮小後の画像から切り抜く範囲（枠より大きくならない）。</param>
/// <param name="OffsetX">切り抜いた画像を置く位置（枠の左端から）。余白が無ければ 0。</param>
/// <param name="OffsetY">切り抜いた画像を置く位置（枠の上端から）。余白が無ければ 0。</param>
public readonly record struct ImagePlacement(int ScaledWidth, int ScaledHeight, PixelRect Crop, int OffsetX, int OffsetY);
