namespace WallpaperChanger.Infrastructure.Imaging;

/// <summary>
/// 読み込む画像の大きさの上限。
/// 壁紙フォルダの画像は信頼できるとは限らないため、細工された画像（展開すると巨大になる画像など）や
/// 極端に大きな画像によるメモリ不足・応答停止を防ぐ。テストでは小さい値に差し替える。
/// </summary>
public sealed class ImageSourceLimits
{
    /// <summary>既定のファイルサイズの上限（256 MiB）。</summary>
    public const long DefaultMaxFileBytes = 256L * 1024 * 1024;

    /// <summary>既定のピクセル数の上限（2 億ピクセル。例: 20000 × 10000）。</summary>
    public const long DefaultMaxPixelCount = 200_000_000;

    /// <summary>ファイルサイズの上限（バイト）。</summary>
    public long MaxFileBytes { get; set; } = DefaultMaxFileBytes;

    /// <summary>ピクセル数（幅 × 高さ）の上限。</summary>
    public long MaxPixelCount { get; set; } = DefaultMaxPixelCount;

    public bool IsAllowedFileSize(long bytes) => bytes <= MaxFileBytes;

    public bool IsAllowedPixelCount(int width, int height) => (long)width * height <= MaxPixelCount;
}

/// <summary>
/// 画像が <see cref="ImageSourceLimits"/> の上限を超えているため読み込まなかったことを表す。
/// </summary>
internal sealed class ImageTooLargeException(string message) : InvalidOperationException(message);
