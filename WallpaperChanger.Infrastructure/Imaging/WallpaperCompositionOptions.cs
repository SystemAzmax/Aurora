namespace WallpaperChanger.Infrastructure.Imaging;

/// <summary>
/// 分割表示の合成画像に関する設定。
/// </summary>
public sealed class WallpaperCompositionOptions
{
    public static string DefaultOutputDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperChanger",
        "Composites");

    /// <summary>合成画像の保存先。</summary>
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    /// <summary>タイル間の間隔（ピクセル）。</summary>
    public int TileGap { get; set; } = 4;

    /// <summary>間隔部分の色（0xRRGGBB）。</summary>
    public int BackgroundColor { get; set; } = 0x202020;

    /// <summary>フォルダ未設定のマスの色（0xRRGGBB）。既定は真っ黒（0x000000）。</summary>
    public int EmptyTileColor { get; set; }

    /// <summary>JPEG の品質 (1～100)。</summary>
    public int JpegQuality { get; set; } = 92;

    /// <summary>モニターごとに残す合成画像の数（設定中の壁紙を削除しないよう 2 以上にする）。</summary>
    public int FilesToKeepPerMonitor { get; set; } = 3;
}
