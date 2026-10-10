namespace WallpaperChanger.Core.Models;

/// <summary>1 台のモニターで壁紙の切り替えがどうなったか。</summary>
public enum WallpaperChangeStatus
{
    /// <summary>壁紙を変更した。</summary>
    Changed,

    /// <summary>壁紙フォルダが登録されていないため変更しなかった。</summary>
    NoFolders,

    /// <summary>壁紙フォルダに対応する画像が無い（フォルダの移動・ドライブの切断を含む）ため変更しなかった。</summary>
    NoImages,

    /// <summary>「前へ」で戻れる壁紙が履歴に無いため変更しなかった。</summary>
    NoPreviousWallpaper,

    /// <summary>表示し直す必要が無いため何もしなかった（1 枚表示の解像度変更など）。</summary>
    NotRequired,
}

/// <summary>1 台のモニターの切り替え結果。</summary>
public sealed record MonitorChangeResult(MonitorInfo Monitor, WallpaperChangeStatus Status);

/// <summary>
/// 壁紙の切り替え（次へ・前へ・表示し直し）の結果。変更しなかったモニターとその理由を画面で伝えるために使う。
/// 変更に失敗したモニターがある場合は、結果ではなく例外で通知される。
/// </summary>
public sealed class WallpaperChangeResult
{
    public WallpaperChangeResult(IReadOnlyList<MonitorChangeResult> monitors)
    {
        Monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
    }

    /// <summary>対象のモニターが無かった結果。</summary>
    public static WallpaperChangeResult Empty { get; } = new([]);

    /// <summary>対象になったモニターごとの結果。</summary>
    public IReadOnlyList<MonitorChangeResult> Monitors { get; }

    /// <summary>1 台でも壁紙を変更したか。</summary>
    public bool AnyChanged => Monitors.Any(m => m.Status == WallpaperChangeStatus.Changed);

    /// <summary>指定した結果になったモニター。</summary>
    public IReadOnlyList<MonitorInfo> MonitorsWith(WallpaperChangeStatus status) =>
        [.. Monitors.Where(m => m.Status == status).Select(m => m.Monitor)];
}
