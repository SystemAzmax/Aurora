using System.Windows.Media;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// プレビュー用に縮小した画像を読み込む。
/// </summary>
public interface IImagePreviewLoader
{
    /// <summary>
    /// 画像を読み込む。読み込めない形式（コーデック未導入の WEBP 等）の場合は null。
    /// 返される ImageSource は Freeze 済みで、任意のスレッドから利用できる。
    /// </summary>
    Task<ImageSource?> LoadAsync(string imagePath, int decodePixelWidth, CancellationToken cancellationToken = default);
}
