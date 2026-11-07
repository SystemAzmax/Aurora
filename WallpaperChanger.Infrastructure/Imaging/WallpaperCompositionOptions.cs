namespace WallpaperChanger.Infrastructure.Imaging;

/// <summary>
/// 合成画像（分割表示と、表示方法を指定した 1 枚表示）に関する設定。
/// </summary>
public sealed class WallpaperCompositionOptions
{
    public static string DefaultOutputDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperChanger",
        "Composites");

    /// <summary>合成画像の保存先。</summary>
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    /// <summary>
    /// マスの画像を同時に読み込む数。16 分割では 1 枚ずつ読むと時間がかかるため、並行して読み込む。
    /// 大きな画像はデコード中に多くのメモリを使うため、上げすぎない。
    /// </summary>
    public int MaxParallelTileLoads { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 4);

    /// <summary>JPEG の品質 (1～100)。</summary>
    public int JpegQuality { get; set; } = 92;

    /// <summary>モニターごとに残す合成画像の数（設定中の壁紙を削除しないよう 2 以上にする）。</summary>
    public int FilesToKeepPerMonitor { get; set; } = 3;

    /// <summary>
    /// 接続されていないモニターの合成画像を残す期間（最後に保存してから）。
    /// ドッキングなどで日常的に抜き差しするモニターでは、再接続時に Windows がその画像を表示するため、すぐには削除しない。
    /// </summary>
    public TimeSpan DisconnectedMonitorRetention { get; set; } = TimeSpan.FromDays(30);
}
