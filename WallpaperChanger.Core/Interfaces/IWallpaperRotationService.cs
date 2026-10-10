using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// モニターごとの設定と履歴に基づき、次／前の壁紙へ切り替える。
/// </summary>
public interface IWallpaperRotationService
{
    /// <summary>壁紙が変更されたときに発生する。</summary>
    event EventHandler<WallpaperChangedEventArgs>? WallpaperChanged;

    /// <summary>
    /// 次の壁紙へ切り替える。monitorId が null の場合は全モニターが対象。
    /// 戻り値はモニターごとの結果（変更しなかったモニターとその理由）。変更に失敗したモニターがある場合は例外になる。
    /// </summary>
    Task<WallpaperChangeResult> NextAsync(string? monitorId = null, CancellationToken cancellationToken = default);

    /// <summary>履歴を遡って前の壁紙へ戻す。monitorId が null の場合は全モニターが対象。戻り値は <see cref="NextAsync"/> と同じ。</summary>
    Task<WallpaperChangeResult> PreviousAsync(string? monitorId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在の表示を、モニターの今の解像度で表示し直す（分割表示の合成画像を作り直す）。
    /// 1 枚表示は OS が拡大縮小するため何もしない。表示中の組が無ければ次の壁紙を表示する。
    /// </summary>
    Task<WallpaperChangeResult> ReapplyAsync(string monitorId, CancellationToken cancellationToken = default);
}
