using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 分割表示用、または表示方法を指定した 1 枚表示用の合成リクエスト。
/// </summary>
/// <param name="MonitorId">対象モニター（出力ファイル名の決定に使う）。</param>
/// <param name="ImagePaths">タイルに配置する画像（左上から右方向、次の行へ、の順）。null のマスは <paramref name="EmptyColor"/> で塗る。</param>
/// <param name="Columns">列数。</param>
/// <param name="Rows">行数。</param>
/// <param name="Width">出力画像の幅（モニターの解像度）。</param>
/// <param name="Height">出力画像の高さ（モニターの解像度）。</param>
/// <param name="Fit">各画像をマスに収める方法。余白は <paramref name="EmptyColor"/> で塗る。</param>
/// <param name="Gap">マスの間隔（ピクセル）。</param>
/// <param name="GapColor">マスの間隔の色（0xRRGGBB）。</param>
/// <param name="EmptyColor">画像の無いマスと、画像がマスを覆わない余白の色（0xRRGGBB）。</param>
public sealed record WallpaperCompositionRequest(
    string MonitorId,
    IReadOnlyList<string?> ImagePaths,
    int Columns,
    int Rows,
    int Width,
    int Height,
    ImageFit Fit = ImageFit.Fill,
    int Gap = 0,
    int GapColor = 0x000000,
    int EmptyColor = 0x000000);

/// <summary>
/// スパン（複数モニターにまたがる 1 枚表示）の合成リクエスト。
/// </summary>
/// <param name="ImagePath">表示する画像。</param>
/// <param name="Monitors">スパンに参加するモニター。</param>
/// <param name="Fit">画像を、モニター全体を囲む範囲に収める方法。</param>
/// <param name="EmptyColor">画像の無い余白の色（0xRRGGBB）。</param>
public sealed record SpanCompositionRequest(
    string ImagePath,
    IReadOnlyList<SpanMonitor> Monitors,
    ImageFit Fit = ImageFit.Fill,
    int EmptyColor = 0x000000);

/// <summary>スパンに参加するモニター。</summary>
/// <param name="MonitorId">モニターの ID（出力ファイル名の決定に使う）。</param>
/// <param name="Bounds">仮想デスクトップ上の領域（切り分ける位置の計算に使う）。</param>
/// <param name="Width">出力画像の幅（モニターの解像度）。</param>
/// <param name="Height">出力画像の高さ（モニターの解像度）。</param>
public sealed record SpanMonitor(string MonitorId, MonitorBounds Bounds, int Width, int Height);

/// <summary>
/// 画像をモニターの解像度の 1 枚の壁紙画像に合成する。
/// IDesktopWallpaper は 1 モニターに 1 枚しか設定できず、表示方法（調整方法・スパン）も全モニター共通のため、
/// 分割表示、表示方法を指定した 1 枚表示、一部のモニターにまたがる 1 枚表示（スパン）は合成画像で実現する。
/// </summary>
public interface IWallpaperComposer
{
    /// <summary>画像を合成してファイルに保存し、そのフルパスを返す。</summary>
    Task<string> ComposeAsync(WallpaperCompositionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 1 枚の画像を、スパンに参加するモニターの位置に合わせて切り分け、モニターごとの壁紙画像として保存する。
    /// Windows の「スパン」は全モニター共通の設定のため、一部のモニターだけでまたがって表示できるよう、モニターごとに画像を作る。
    /// </summary>
    /// <returns>モニターの ID ごとの、保存した画像のフルパス。</returns>
    Task<IReadOnlyDictionary<string, string>> ComposeSpanAsync(SpanCompositionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 接続されていないモニターの合成画像のうち、一定期間更新されていないものを削除する（後片付けのため、失敗しても例外にしない）。
    /// </summary>
    /// <param name="connectedMonitorIds">現在接続されているモニターの ID。これらの合成画像は削除しない。</param>
    void DeleteDisconnectedMonitorFiles(IReadOnlyCollection<string> connectedMonitorIds);
}
