namespace WallpaperChanger.Core.Models;

/// <summary>
/// 自動切り替えを見合わせる条件。不変オブジェクトとして扱い、変更は with 式で行う。
/// 条件に当てはまる間は自動切り替えを待ち、当てはまらなくなったら切り替える（手動の切り替えは止めない）。
/// </summary>
/// <remarks>
/// 設定ファイルのソース生成による読み込みでは、ファイルに無い init プロパティは初期値ではなく既定値（false）になる。
/// この設定を追加する前の設定ファイルでも既定の動きになるよう、<see cref="AppSettings"/> では 1 つのオブジェクトにまとめ、
/// ファイルに無ければ（null なら）<see cref="AppSettings.Normalize"/> で既定値に置き換える。
/// </remarks>
public sealed record SkipConditions
{
    /// <summary>全画面表示のアプリ（ゲーム・動画など）やプレゼンテーションの実行中は切り替えないか。</summary>
    public bool WhenFullScreen { get; init; } = true;

    /// <summary>
    /// バッテリーで動作している間は切り替えないか。
    /// Windows のスライドショーも、既定ではバッテリー駆動中に止まるため、それに合わせて既定で有効にする。
    /// </summary>
    public bool OnBattery { get; init; } = true;
}
