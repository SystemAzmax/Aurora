namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 分割表示用の合成リクエスト。
/// </summary>
/// <param name="MonitorId">対象モニター（出力ファイル名の決定に使う）。</param>
/// <param name="ImagePaths">タイルに配置する画像（左上から右方向、次の行へ、の順）。null のマスは黒で塗る。</param>
/// <param name="Columns">列数。</param>
/// <param name="Rows">行数。</param>
/// <param name="Width">出力画像の幅（モニターの解像度）。</param>
/// <param name="Height">出力画像の高さ（モニターの解像度）。</param>
public sealed record WallpaperCompositionRequest(
    string MonitorId,
    IReadOnlyList<string?> ImagePaths,
    int Columns,
    int Rows,
    int Width,
    int Height);

/// <summary>
/// 複数の画像を 1 枚の壁紙画像に合成する。
/// IDesktopWallpaper は 1 モニターに 1 枚しか設定できないため、分割表示は合成画像で実現する。
/// </summary>
public interface IWallpaperComposer
{
    /// <summary>画像を合成してファイルに保存し、そのフルパスを返す。</summary>
    Task<string> ComposeAsync(WallpaperCompositionRequest request, CancellationToken cancellationToken = default);
}
